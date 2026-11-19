#!/usr/bin/env python3
"""Minimal SWF/ABC dumper: writes pseudo-source and raw disassembly per class.

Usage: abcdump.py <file.swf> <outdir>
Read-only against the SWF; all output goes to <outdir>.
"""
import sys, zlib, lzma, struct, os, json

SKIP_PREFIXES = ('mx.', 'spark.', 'flash.', 'flashx.', 'fl.', 'org.osmf.', 'com.adobe.', 'adobe.', 'air.', 'avmplus.')


class R:
    def __init__(s, d, p=0):
        s.d = d; s.p = p

    def u8(s):
        v = s.d[s.p]; s.p += 1; return v

    def u16(s):
        v = struct.unpack_from('<H', s.d, s.p)[0]; s.p += 2; return v

    def u32f(s):
        v = struct.unpack_from('<I', s.d, s.p)[0]; s.p += 4; return v

    def s24(s):
        b = s.d[s.p:s.p + 3]; s.p += 3
        v = b[0] | (b[1] << 8) | (b[2] << 16)
        if v & 0x800000: v -= 0x1000000
        return v

    def u30(s):
        r = 0; sh = 0
        while True:
            b = s.d[s.p]; s.p += 1
            r |= (b & 0x7f) << sh; sh += 7
            if not (b & 0x80) or sh >= 35: break
        return r & 0xFFFFFFFF

    def s32(s):
        v = s.u30()
        if v & 0x80000000: v -= 0x100000000
        return v

    def d64(s):
        v = struct.unpack_from('<d', s.d, s.p)[0]; s.p += 8; return v

    def bytes(s, n):
        v = s.d[s.p:s.p + n]; s.p += n; return v


OPS = {
    0x01: ('bkpt', ''), 0x02: ('nop', ''), 0x03: ('throw', ''), 0x04: ('getsuper', 'm'), 0x05: ('setsuper', 'm'),
    0x06: ('dxns', 's'), 0x07: ('dxnslate', ''), 0x08: ('kill', 'u'), 0x09: ('label', ''),
    0x0C: ('ifnlt', 'j'), 0x0D: ('ifnle', 'j'), 0x0E: ('ifngt', 'j'), 0x0F: ('ifnge', 'j'), 0x10: ('jump', 'j'),
    0x11: ('iftrue', 'j'), 0x12: ('iffalse', 'j'), 0x13: ('ifeq', 'j'), 0x14: ('ifne', 'j'), 0x15: ('iflt', 'j'),
    0x16: ('ifle', 'j'), 0x17: ('ifgt', 'j'), 0x18: ('ifge', 'j'), 0x19: ('ifstricteq', 'j'), 0x1A: ('ifstrictne', 'j'),
    0x1B: ('lookupswitch', 'S'), 0x1C: ('pushwith', ''), 0x1D: ('popscope', ''), 0x1E: ('nextname', ''),
    0x1F: ('hasnext', ''), 0x20: ('pushnull', ''), 0x21: ('pushundefined', ''), 0x23: ('nextvalue', ''),
    0x24: ('pushbyte', 'b'), 0x25: ('pushshort', 'h'), 0x26: ('pushtrue', ''), 0x27: ('pushfalse', ''),
    0x28: ('pushnan', ''), 0x29: ('pop', ''), 0x2A: ('dup', ''), 0x2B: ('swap', ''), 0x2C: ('pushstring', 's'),
    0x2D: ('pushint', 'i'), 0x2E: ('pushuint', 'I'), 0x2F: ('pushdouble', 'd'), 0x30: ('pushscope', ''),
    0x31: ('pushnamespace', 'n'), 0x32: ('hasnext2', 'uu'),
    0x35: ('li8', ''), 0x36: ('li16', ''), 0x37: ('li32', ''), 0x38: ('lf32', ''), 0x39: ('lf64', ''),
    0x3A: ('si8', ''), 0x3B: ('si16', ''), 0x3C: ('si32', ''), 0x3D: ('sf32', ''), 0x3E: ('sf64', ''),
    0x40: ('newfunction', 'u'), 0x41: ('call', 'u'), 0x42: ('construct', 'u'), 0x43: ('callmethod', 'uu'),
    0x44: ('callstatic', 'uu'), 0x45: ('callsuper', 'mu'), 0x46: ('callproperty', 'mu'), 0x47: ('returnvoid', ''),
    0x48: ('returnvalue', ''), 0x49: ('constructsuper', 'u'), 0x4A: ('constructprop', 'mu'),
    0x4C: ('callproplex', 'mu'), 0x4E: ('callsupervoid', 'mu'), 0x4F: ('callpropvoid', 'mu'),
    0x50: ('sxi1', ''), 0x51: ('sxi8', ''), 0x52: ('sxi16', ''), 0x53: ('applytype', 'u'),
    0x55: ('newobject', 'u'), 0x56: ('newarray', 'u'), 0x57: ('newactivation', ''), 0x58: ('newclass', 'u'),
    0x59: ('getdescendants', 'm'), 0x5A: ('newcatch', 'u'), 0x5D: ('findpropstrict', 'm'),
    0x5E: ('findproperty', 'm'), 0x5F: ('finddef', 'm'), 0x60: ('getlex', 'm'), 0x61: ('setproperty', 'm'),
    0x62: ('getlocal', 'u'), 0x63: ('setlocal', 'u'), 0x64: ('getglobalscope', ''), 0x65: ('getscopeobject', 'B'),
    0x66: ('getproperty', 'm'), 0x68: ('initproperty', 'm'), 0x6A: ('deleteproperty', 'm'),
    0x6C: ('getslot', 'u'), 0x6D: ('setslot', 'u'), 0x6E: ('getglobalslot', 'u'), 0x6F: ('setglobalslot', 'u'),
    0x70: ('convert_s', ''), 0x71: ('esc_xelem', ''), 0x72: ('esc_xattr', ''), 0x73: ('convert_i', ''),
    0x74: ('convert_u', ''), 0x75: ('convert_d', ''), 0x76: ('convert_b', ''), 0x77: ('convert_o', ''),
    0x78: ('checkfilter', ''), 0x80: ('coerce', 'm'), 0x82: ('coerce_a', ''), 0x85: ('coerce_s', ''),
    0x86: ('astype', 'm'), 0x87: ('astypelate', ''), 0x90: ('negate', ''), 0x91: ('increment', ''),
    0x92: ('inclocal', 'u'), 0x93: ('decrement', ''), 0x94: ('declocal', 'u'), 0x95: ('typeof', ''),
    0x96: ('not', ''), 0x97: ('bitnot', ''),
    0xA0: ('add', ''), 0xA1: ('subtract', ''), 0xA2: ('multiply', ''), 0xA3: ('divide', ''), 0xA4: ('modulo', ''),
    0xA5: ('lshift', ''), 0xA6: ('rshift', ''), 0xA7: ('urshift', ''), 0xA8: ('bitand', ''), 0xA9: ('bitor', ''),
    0xAA: ('bitxor', ''), 0xAB: ('equals', ''), 0xAC: ('strictequals', ''), 0xAD: ('lessthan', ''),
    0xAE: ('lessequals', ''), 0xAF: ('greaterthan', ''), 0xB0: ('greaterequals', ''), 0xB1: ('instanceof', ''),
    0xB2: ('istype', 'm'), 0xB3: ('istypelate', ''), 0xB4: ('in', ''),
    0xC0: ('increment_i', ''), 0xC1: ('decrement_i', ''), 0xC2: ('inclocal_i', 'u'), 0xC3: ('declocal_i', 'u'),
    0xC4: ('negate_i', ''), 0xC5: ('add_i', ''), 0xC6: ('subtract_i', ''), 0xC7: ('multiply_i', ''),
    0xD0: ('getlocal_0', ''), 0xD1: ('getlocal_1', ''), 0xD2: ('getlocal_2', ''), 0xD3: ('getlocal_3', ''),
    0xD4: ('setlocal_0', ''), 0xD5: ('setlocal_1', ''), 0xD6: ('setlocal_2', ''), 0xD7: ('setlocal_3', ''),
    0xEF: ('debug', 'D'), 0xF0: ('debugline', 'u'), 0xF1: ('debugfile', 's'), 0xF2: ('bkptline', ''),
}

BINOPS = {
    'add': '+', 'subtract': '-', 'multiply': '*', 'divide': '/', 'modulo': '%', 'lshift': '<<', 'rshift': '>>',
    'urshift': '>>>', 'bitand': '&', 'bitor': '|', 'bitxor': '^', 'equals': '==', 'strictequals': '===',
    'lessthan': '<', 'lessequals': '<=', 'greaterthan': '>', 'greaterequals': '>=', 'instanceof': 'instanceof',
    'in': 'in', 'add_i': '+', 'subtract_i': '-', 'multiply_i': '*',
}
IFOPS = {
    'ifeq': '{a} == {b}', 'ifne': '{a} != {b}', 'iflt': '{a} < {b}', 'ifle': '{a} <= {b}', 'ifgt': '{a} > {b}',
    'ifge': '{a} >= {b}', 'ifstricteq': '{a} === {b}', 'ifstrictne': '{a} !== {b}', 'ifnlt': '!({a} < {b})',
    'ifnle': '!({a} <= {b})', 'ifngt': '!({a} > {b})', 'ifnge': '!({a} >= {b})',
}
TERMINAL = ('jump', 'returnvoid', 'returnvalue', 'throw', 'lookupswitch')


def lit(s):
    return json.dumps(s, ensure_ascii=False)


class Abc:
    def __init__(self, data):
        r = R(data)
        self.minor = r.u16(); self.major = r.u16()
        n = r.u30(); self.ints = [0] + [r.s32() for _ in range(max(0, n - 1))]
        n = r.u30(); self.uints = [0] + [r.u30() for _ in range(max(0, n - 1))]
        n = r.u30(); self.doubles = [float('nan')] + [r.d64() for _ in range(max(0, n - 1))]
        n = r.u30(); self.strings = ['']
        for _ in range(max(0, n - 1)):
            l = r.u30(); self.strings.append(r.bytes(l).decode('utf8', 'replace'))
        n = r.u30(); self.nss = [(0, 0)]
        for _ in range(max(0, n - 1)):
            k = r.u8(); self.nss.append((k, r.u30()))
        n = r.u30(); self.nssets = [[]]
        for _ in range(max(0, n - 1)):
            c = r.u30(); self.nssets.append([r.u30() for _ in range(c)])
        n = r.u30(); self.mns = [(0,)]
        for _ in range(max(0, n - 1)):
            k = r.u8()
            if k in (0x07, 0x0D): self.mns.append((k, r.u30(), r.u30()))
            elif k in (0x0F, 0x10): self.mns.append((k, r.u30()))
            elif k in (0x11, 0x12): self.mns.append((k,))
            elif k in (0x09, 0x0E): self.mns.append((k, r.u30(), r.u30()))
            elif k in (0x1B, 0x1C): self.mns.append((k, r.u30()))
            elif k == 0x1D:
                q = r.u30(); c = r.u30(); self.mns.append((k, q, [r.u30() for _ in range(c)]))
            else: raise ValueError('bad multiname kind %x' % k)
        n = r.u30(); self.methods = []
        for _ in range(n):
            pc = r.u30(); ret = r.u30(); pts = [r.u30() for _ in range(pc)]
            name = r.u30(); flags = r.u8(); opts = []; pnames = []
            if flags & 0x08:
                oc = r.u30(); opts = [(r.u30(), r.u8()) for _ in range(oc)]
            if flags & 0x80:
                pnames = [r.u30() for _ in range(pc)]
            self.methods.append(dict(ptypes=pts, ret=ret, name=name, flags=flags, opts=opts, pnames=pnames, body=None))
        n = r.u30()
        for _ in range(n):
            r.u30(); c = r.u30()
            for _ in range(c * 2): r.u30()
        n = r.u30(); self.instances = []
        for _ in range(n):
            name = r.u30(); sup = r.u30(); flags = r.u8()
            if flags & 0x08: r.u30()
            ic = r.u30(); intf = [r.u30() for _ in range(ic)]
            iinit = r.u30(); traits = self.traits(r)
            self.instances.append(dict(name=name, sup=sup, flags=flags, intf=intf, iinit=iinit, traits=traits))
        self.classes = []
        for _ in range(n):
            cinit = r.u30(); self.classes.append(dict(cinit=cinit, traits=self.traits(r)))
        n = r.u30(); self.scripts = []
        for _ in range(n):
            init = r.u30(); self.scripts.append(dict(init=init, traits=self.traits(r)))
        n = r.u30()
        for _ in range(n):
            m = r.u30(); ms = r.u30(); lc = r.u30(); isd = r.u30(); msd = r.u30()
            cl = r.u30(); code = r.bytes(cl)
            ec = r.u30(); exc = [(r.u30(), r.u30(), r.u30(), r.u30(), r.u30()) for _ in range(ec)]
            traits = self.traits(r)
            self.methods[m]['body'] = dict(code=code, exc=exc, traits=traits, locals=lc)

    def traits(self, r):
        out = []
        for _ in range(r.u30()):
            name = r.u30(); kb = r.u8(); kind = kb & 0x0F; attrs = kb >> 4
            t = dict(name=name, kind=kind)
            if kind in (0, 6):
                t['slot'] = r.u30(); t['type'] = r.u30(); vi = r.u30(); t['vindex'] = vi
                t['vkind'] = r.u8() if vi else None
            elif kind == 4:
                t['slot'] = r.u30(); t['classi'] = r.u30()
            elif kind == 5:
                t['slot'] = r.u30(); t['method'] = r.u30()
            elif kind in (1, 2, 3):
                t['disp'] = r.u30(); t['method'] = r.u30()
            else: raise ValueError('bad trait kind %d' % kind)
            if attrs & 0x4:
                for _ in range(r.u30()): r.u30()
            out.append(t)
        return out

    def ns(self, i):
        return self.strings[self.nss[i][1]] if i else '*'

    def vis(self, mi):
        m = self.mns[mi]
        if m[0] in (0x07, 0x0D) and m[1]:
            return {0x16: 'public', 0x05: 'private', 0x18: 'protected', 0x17: 'internal', 0x1A: 'protected'}.get(self.nss[m[1]][0], 'ns')
        return ''

    def mn(self, i, full=False):
        if i == 0: return '*'
        m = self.mns[i]; k = m[0]
        if k in (0x07, 0x0D):
            name = self.strings[m[2]] if m[2] else '*'
            if full:
                ns = self.ns(m[1])
                return (ns + '.' + name) if ns and ns != '*' else name
            return name
        if k in (0x0F, 0x10): return self.strings[m[1]]
        if k in (0x11, 0x12): return '[rt]'
        if k in (0x09, 0x0E): return self.strings[m[1]] if m[1] else '*'
        if k in (0x1B, 0x1C): return '[]'
        if k == 0x1D:
            return self.mn(m[1], full) + '.<' + ','.join(self.mn(p, full) for p in m[2]) + '>'
        return '?'

    def const(self, vi, vk):
        if vk == 0x03: return str(self.ints[vi])
        if vk == 0x04: return str(self.uints[vi])
        if vk == 0x06: return repr(self.doubles[vi])
        if vk == 0x01: return lit(self.strings[vi])
        if vk == 0x0B: return 'true'
        if vk == 0x0A: return 'false'
        if vk == 0x0C: return 'null'
        if vk == 0x00: return 'undefined'
        return 'ns:' + self.ns(vi)

    def sig(self, mi, name):
        m = self.methods[mi]; ps = []
        no = len(m['ptypes']) - len(m['opts'])
        for i, pt in enumerate(m['ptypes']):
            pn = self.strings[m['pnames'][i]] if m['pnames'] else 'p%d' % (i + 1)
            s = '%s:%s' % (pn, self.mn(pt))
            if i >= no:
                o = m['opts'][i - no]; s += ' = ' + self.const(o[0], o[1])
            ps.append(s)
        if m['flags'] & 0x04: ps.append('...rest')
        return 'function %s(%s):%s' % (name, ', '.join(ps), self.mn(m['ret']))

    def decode(self, code):
        r = R(code); ins = []
        while r.p < len(code):
            off = r.p; op = r.u8()
            name, fmt = OPS.get(op, ('op_%02x' % op, ''))
            args = []
            for f in fmt:
                if f in 'umsiIdn': args.append(r.u30())
                elif f == 'h':
                    v = r.u30()
                    if v & 0x80000000: v -= 0x100000000
                    elif v & 0x20000000: v -= 0x40000000
                    args.append(v)
                elif f == 'b':
                    v = r.u8(); args.append(v - 256 if v > 127 else v)
                elif f == 'B': args.append(r.u8())
                elif f == 'j':
                    o = r.s24(); args.append(r.p + o)
                elif f == 'S':
                    d = r.s24(); c = r.u30()
                    args.append(off + d); args.append([off + r.s24() for _ in range(c + 1)])
                elif f == 'D':
                    args.extend([r.u8(), r.u30(), r.u8(), r.u30()])
            ins.append((off, name, fmt, args))
        return ins

    def argstr(self, fmt, args):
        out = []
        for f, a in zip(fmt if fmt not in ('S', 'D') else ['x'] * len(args), args):
            if f == 'm': out.append(self.mn(a, True))
            elif f == 's': out.append(lit(self.strings[a]))
            elif f == 'i': out.append(str(self.ints[a]))
            elif f == 'I': out.append(str(self.uints[a]))
            elif f == 'd': out.append(repr(self.doubles[a]))
            elif f == 'n': out.append(self.ns(a))
            elif f == 'j': out.append('L%d' % a)
            elif f == 'x' and isinstance(a, list): out.append('[' + ','.join('L%d' % x for x in a) + ']')
            else: out.append(str(a))
        return ' '.join(out)

    def disasm(self, mi, out, ind):
        b = self.methods[mi]['body']
        if not b: return
        for e in b['exc']:
            out.append('%s; try [%d,%d) -> L%d catch %s' % (ind, e[0], e[1], e[2], self.mn(e[3], True)))
        for off, name, fmt, args in self.decode(b['code']):
            if name in ('debug', 'debugline', 'debugfile'): continue
            out.append('%s%5d  %s %s' % (ind, off, name, self.argstr(fmt, args)))

    def pseudo(self, mi, out, ind, closures):
        m = self.methods[mi]; b = m['body']
        if not b:
            out.append(ind + '// (no body)'); return
        ins = self.decode(b['code'])
        names = {0: 'this'}
        for i in range(len(m['ptypes'])):
            names[i + 1] = self.strings[m['pnames'][i]] if m['pnames'] else 'p%d' % (i + 1)
        for off, name, fmt, a in ins:
            if name == 'debug' and a[0] == 1 and a[2] not in names:
                names[a[2]] = self.strings[a[1]]
        slotnames = {t['slot']: self.mn(t['name']) for t in b['traits'] if 'slot' in t}
        labels = set()
        for off, name, fmt, a in ins:
            if fmt == 'j': labels.add(a[0])
            elif fmt == 'S':
                labels.add(a[0]); labels.update(a[1])
        saved = {}
        for e in b['exc']:
            labels.add(e[2]); saved[e[2]] = ['$exception']
            out.append('%s// try [L%d..L%d) catch(%s) -> L%d' % (ind, e[0], e[1], self.mn(e[3]), e[2]))
        st = []

        def pop():
            return st.pop() if st else '?'

        def cl(x):
            return x[1:] if x.startswith('\u00a7') else x

        def loc(n):
            return names.get(n, 'loc%d' % n)

        def emit(s):
            out.append(ind + s)

        def popargs(n):
            a = [cl(pop()) for _ in range(n)]; a.reverse(); return ', '.join(a)

        def member(mi_):
            """Pops runtime name parts + object; returns accessor string."""
            k = self.mns[mi_][0] if mi_ else 0
            if k in (0x1B, 0x1C):
                idx = cl(pop()); obj = cl(pop()); return '%s[%s]' % (obj, idx)
            if k in (0x11, 0x12):
                idx = cl(pop()); pop(); obj = cl(pop()); return '%s[%s]' % (obj, idx)
            if k in (0x0F, 0x10): pop()
            obj = pop(); nm = self.mn(mi_)
            if obj.startswith('\u00a7'): return nm
            return '%s.%s' % (obj, nm)

        def branch(t):
            if t not in saved: saved[t] = list(st)

        prev_terminal = False
        for off, name, fmt, a in ins:
            if off in labels:
                if prev_terminal:
                    st[:] = list(saved.get(off, []))
                elif off in saved and len(saved[off]) == len(st):
                    for i in range(len(st)):
                        if saved[off][i] != st[i]:
                            st[i] = '\u03c6(%s, %s)' % (cl(saved[off][i]), cl(st[i]))
                emit('L%d:' % off + (('   /* stack: %s */' % ' | '.join(cl(x) for x in st)) if st else ''))
            prev_terminal = name in TERMINAL
            try:
                if name in ('debug', 'debugline', 'debugfile', 'label', 'nop', 'kill', 'checkfilter', 'coerce_a',
                            'coerce_s', 'coerce', 'popscope', 'dxns', 'convert_o', 'bkpt', 'bkptline'):
                    pass
                elif name in ('pushscope', 'pushwith'): pop()
                elif name.startswith('getlocal'):
                    st.append(loc(a[0] if a else int(name[-1])))
                elif name.startswith('setlocal'):
                    emit('%s = %s;' % (loc(a[0] if a else int(name[-1])), cl(pop())))
                elif name == 'pushnull': st.append('null')
                elif name == 'pushundefined': st.append('undefined')
                elif name == 'pushtrue': st.append('true')
                elif name == 'pushfalse': st.append('false')
                elif name == 'pushnan': st.append('NaN')
                elif name in ('pushbyte', 'pushshort'): st.append(str(a[0]))
                elif name == 'pushstring': st.append(lit(self.strings[a[0]]))
                elif name == 'pushint': st.append(str(self.ints[a[0]]))
                elif name == 'pushuint': st.append(str(self.uints[a[0]]))
                elif name == 'pushdouble': st.append(repr(self.doubles[a[0]]))
                elif name == 'pushnamespace': st.append('ns:' + self.ns(a[0]))
                elif name == 'getglobalscope': st.append('global')
                elif name == 'getscopeobject': st.append('scope%d' % a[0])
                elif name in ('findpropstrict', 'findproperty', 'finddef'):
                    k = self.mns[a[0]][0]
                    if k in (0x1B, 0x1C, 0x11, 0x12): pop()
                    st.append('\u00a7' + self.mn(a[0]))
                elif name == 'getlex': st.append(self.mn(a[0]))
                elif name == 'getproperty': st.append(member(a[0]))
                elif name in ('setproperty', 'initproperty'):
                    v = cl(pop()); emit('%s = %s;' % (member(a[0]), v))
                elif name == 'deleteproperty': st.append('delete ' + member(a[0]))
                elif name == 'getdescendants': st.append(cl(pop()) + '..' + self.mn(a[0]))
                elif name == 'getsuper': pop(); st.append('super.' + self.mn(a[0]))
                elif name == 'setsuper':
                    v = cl(pop()); pop(); emit('super.%s = %s;' % (self.mn(a[0]), v))
                elif name in ('callproperty', 'callproplex'):
                    args = popargs(a[1]); st.append('%s(%s)' % (member(a[0]), args))
                elif name == 'callpropvoid':
                    args = popargs(a[1]); emit('%s(%s);' % (member(a[0]), args))
                elif name == 'callsuper':
                    args = popargs(a[1]); pop(); st.append('super.%s(%s)' % (self.mn(a[0]), args))
                elif name == 'callsupervoid':
                    args = popargs(a[1]); pop(); emit('super.%s(%s);' % (self.mn(a[0]), args))
                elif name == 'constructprop':
                    args = popargs(a[1]); st.append('new %s(%s)' % (member(a[0]), args))
                elif name == 'construct':
                    args = popargs(a[0]); st.append('new %s(%s)' % (cl(pop()), args))
                elif name == 'constructsuper':
                    args = popargs(a[0]); pop(); emit('super(%s);' % args)
                elif name == 'call':
                    args = popargs(a[0]); pop(); st.append('%s(%s)' % (cl(pop()), args))
                elif name in ('callstatic', 'callmethod'):
                    args = popargs(a[1]); pop(); st.append('%s#%d(%s)' % (name, a[0], args))
                elif name == 'newobject':
                    kv = [cl(pop()) for _ in range(a[0] * 2)]; kv.reverse()
                    st.append('{' + ', '.join('%s: %s' % (kv[i], kv[i + 1]) for i in range(0, len(kv), 2)) + '}')
                elif name == 'newarray': st.append('[' + popargs(a[0]) + ']')
                elif name == 'newactivation': st.append('activation')
                elif name == 'newclass': pop(); st.append('class#%d' % a[0])
                elif name == 'newfunction':
                    closures.append(a[0]); st.append('function#%d' % a[0])
                elif name == 'newcatch': st.append('catchscope')
                elif name == 'applytype':
                    args = popargs(a[0]); st.append('%s.<%s>' % (cl(pop()), args))
                elif name == 'getslot':
                    o = cl(pop())
                    st.append(slotnames.get(a[0], 'slot%d' % a[0]) if o in ('activation', 'scope1', 'catchscope') or o.startswith('scope') else '%s.slot%d' % (o, a[0]))
                elif name == 'setslot':
                    v = cl(pop()); o = cl(pop())
                    emit('%s = %s;' % (slotnames.get(a[0], 'slot%d' % a[0]) if o.startswith(('activation', 'scope', 'catchscope')) else '%s.slot%d' % (o, a[0]), v))
                elif name in ('getglobalslot',): st.append('globalslot%d' % a[0])
                elif name in ('setglobalslot',): emit('globalslot%d = %s;' % (a[0], cl(pop())))
                elif name == 'convert_i': st.append('int(%s)' % cl(pop()))
                elif name == 'convert_u': st.append('uint(%s)' % cl(pop()))
                elif name == 'convert_d': st.append('Number(%s)' % cl(pop()))
                elif name == 'convert_b': st.append('Boolean(%s)' % cl(pop()))
                elif name == 'convert_s': st.append('String(%s)' % cl(pop()))
                elif name in ('esc_xelem', 'esc_xattr'): st.append('%s(%s)' % (name, cl(pop())))
                elif name == 'astype': st.append('(%s as %s)' % (cl(pop()), self.mn(a[0])))
                elif name == 'astypelate':
                    t = cl(pop()); st.append('(%s as %s)' % (cl(pop()), t))
                elif name == 'istype': st.append('(%s is %s)' % (cl(pop()), self.mn(a[0])))
                elif name == 'istypelate':
                    t = cl(pop()); st.append('(%s is %s)' % (cl(pop()), t))
                elif name in BINOPS:
                    y = cl(pop()); x = cl(pop()); st.append('(%s %s %s)' % (x, BINOPS[name], y))
                elif name == 'not': st.append('!' + cl(pop()))
                elif name in ('negate', 'negate_i'): st.append('-' + cl(pop()))
                elif name == 'bitnot': st.append('~' + cl(pop()))
                elif name in ('increment', 'increment_i'): st.append('(%s + 1)' % cl(pop()))
                elif name in ('decrement', 'decrement_i'): st.append('(%s - 1)' % cl(pop()))
                elif name == 'typeof': st.append('typeof(%s)' % cl(pop()))
                elif name in ('inclocal', 'inclocal_i'): emit('%s++;' % loc(a[0]))
                elif name in ('declocal', 'declocal_i'): emit('%s--;' % loc(a[0]))
                elif name == 'dup':
                    v = pop(); st.append(v); st.append(v)
                elif name == 'swap':
                    y = pop(); x = pop(); st.append(y); st.append(x)
                elif name == 'pop':
                    v = cl(pop())
                    if '(' in v: emit(v + ';')
                elif name == 'returnvalue': emit('return %s;' % cl(pop()))
                elif name == 'returnvoid': emit('return;')
                elif name == 'throw': emit('throw %s;' % cl(pop()))
                elif name in IFOPS:
                    y = cl(pop()); x = cl(pop()); branch(a[0])
                    emit('if (%s) goto L%d;' % (IFOPS[name].format(a=x, b=y), a[0]))
                elif name == 'iftrue':
                    x = cl(pop()); branch(a[0]); emit('if (%s) goto L%d;' % (x, a[0]))
                elif name == 'iffalse':
                    x = cl(pop()); branch(a[0]); emit('if (!(%s)) goto L%d;' % (x, a[0]))
                elif name == 'jump':
                    branch(a[0]); emit('goto L%d;' % a[0])
                elif name == 'lookupswitch':
                    x = cl(pop()); branch(a[0])
                    for t in a[1]: branch(t)
                    emit('switch (%s) { %s default: L%d }' % (x, ' '.join('%d: L%d' % (i, t) for i, t in enumerate(a[1])), a[0]))
                elif name == 'hasnext2': st.append('hasnext2(%s, %s)' % (loc(a[0]), loc(a[1])))
                elif name in ('nextname', 'nextvalue', 'hasnext'):
                    i = cl(pop()); o = cl(pop()); st.append('%s(%s, %s)' % (name, o, i))
                else:
                    emit('/* %s %s */' % (name, self.argstr(fmt, a)))
            except Exception as ex:  # keep going on odd bytecode
                emit('/* !! %s at %d: %s */' % (name, off, ex))


def read_swf(path):
    d = open(path, 'rb').read()
    sig = d[:3]
    if sig == b'CWS': body = zlib.decompress(d[8:])
    elif sig == b'ZWS':
        body = lzma.LZMADecompressor(lzma.FORMAT_RAW, filters=[lzma._decode_filter_properties(lzma.FILTER_LZMA1, d[12:17])]).decompress(d[17:])
    elif sig == b'FWS': body = d[8:]
    else: raise ValueError('not a SWF')
    nbits = body[0] >> 3
    p = (5 + 4 * nbits + 7) // 8 + 4
    tags = []
    while p < len(body):
        cl = struct.unpack_from('<H', body, p)[0]; p += 2
        code = cl >> 6; ln = cl & 0x3f
        if ln == 0x3f:
            ln = struct.unpack_from('<I', body, p)[0]; p += 4
        tags.append((code, body[p:p + ln])); p += ln
        if code == 0: break
    return tags


def fname(s):
    return ''.join(c if c.isalnum() or c in '._-$' else '_' for c in s)


def main():
    swf, outdir = sys.argv[1], sys.argv[2]
    for sub in ('src', 'asm', 'bin'):
        os.makedirs(os.path.join(outdir, sub), exist_ok=True)
    tags = read_swf(swf)
    symbols = {}; index = []; binaries = []
    abcs = []
    for code, data in tags:
        if code == 82:
            e = data.index(b'\0', 4); abcs.append((data[4:e].decode('utf8', 'replace'), data[e + 1:]))
        elif code == 72: abcs.append(('', data))
        elif code == 76:
            r = R(data)
            for _ in range(r.u16()):
                i = r.u16(); e = data.index(b'\0', r.p); symbols[i] = data[r.p:e].decode('utf8', 'replace'); r.p = e + 1
        elif code == 87:
            binaries.append((struct.unpack_from('<H', data, 0)[0], data[6:]))
    for i, b in binaries:
        open(os.path.join(outdir, 'bin', fname('%d_%s.bin' % (i, symbols.get(i, '')))), 'wb').write(b)
    ndump = 0
    for abcname, data in abcs:
        abc = Abc(data)
        for ci, inst in enumerate(abc.instances):
            fqn = abc.mn(inst['name'], True); sup = abc.mn(inst['sup'], True)
            skip = fqn.startswith(SKIP_PREFIXES)
            index.append('%s%s : %s' % ('  ' if skip else '* ', fqn, sup))
            if skip: continue
            ndump += 1
            src = []; asm = []
            kind = 'interface' if inst['flags'] & 0x04 else 'class'
            hdr = '%s %s extends %s' % (kind, fqn, sup)
            if inst['intf']: hdr += ' implements ' + ', '.join(abc.mn(x) for x in inst['intf'])
            src.append(hdr + ' {'); asm.append(hdr)

            def dump_method(mi, label, done):
                if mi in done: return
                done.add(mi)
                src.append(''); src.append('  ' + abc.sig(mi, label) + ' {')
                closures = []
                abc.pseudo(mi, src, '    ', closures)
                src.append('  }')
                asm.append(''); asm.append('  ' + abc.sig(mi, label))
                abc.disasm(mi, asm, '    ')
                for c in closures: dump_method(c, 'function#%d' % c, done)

            done = set()
            for static, traits in ((True, abc.classes[ci]['traits']), (False, inst['traits'])):
                pre = 'static ' if static else ''
                for t in traits:
                    if t['kind'] in (0, 6):
                        v = (' = ' + abc.const(t['vindex'], t['vkind'])) if t['vindex'] else ''
                        src.append('  %s %s%s %s:%s%s;' % (abc.vis(t['name']), pre, 'const' if t['kind'] == 6 else 'var', abc.mn(t['name']), abc.mn(t['type']), v))
            dump_method(abc.classes[ci]['cinit'], 'static$cinit', done)
            dump_method(inst['iinit'], 'constructor', done)
            for static, traits in ((True, abc.classes[ci]['traits']), (False, inst['traits'])):
                pre = 'static ' if static else ''
                for t in traits:
                    if t['kind'] in (1, 2, 3, 5):
                        k = {1: '', 2: 'get ', 3: 'set ', 5: ''}[t['kind']]
                        dump_method(t['method'], '%s %s%s%s' % (abc.vis(t['name']), pre, k, abc.mn(t['name'])), done)
            src.append('}')
            open(os.path.join(outdir, 'src', fname(fqn) + '.as'), 'w', encoding='utf8').write('\n'.join(src) + '\n')
            open(os.path.join(outdir, 'asm', fname(fqn) + '.asm'), 'w', encoding='utf8').write('\n'.join(asm) + '\n')
    open(os.path.join(outdir, 'classes.txt'), 'w', encoding='utf8').write('\n'.join(index) + '\n')
    print('abc blocks: %d, classes: %d, dumped: %d, binaries: %d' % (len(abcs), len(index), ndump, len(binaries)))


if __name__ == '__main__':
    main()
