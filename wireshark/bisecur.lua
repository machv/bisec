--local string = require("string")

bisecur_protocol = Proto("BiSecur",  "BiSecur Protocol")

sender = ProtoField.string("bisecur.sender", "Sender", base.ASCII)
receiver = ProtoField.string("bisecur.receiver", "Receiver", base.ASCII)
body_length = ProtoField.uint16("bisecur.body_length", "Body Length", base.DEC)
tag = ProtoField.uint8("bisecur.tag", "Tag", base.DEC)
token = ProtoField.string("bisecur.token", "Token", base.ASCII)
command = ProtoField.string("bisecur.command", "Command", base.ASCII)
payload = ProtoField.string("bisecur.payload", "Payload", base.ASCII)
response = ProtoField.string("bisecur.response", "Response", base.ASCII)

-- checksums
field_checksums = ProtoField.uint8("bisecur.checksums", "Checksums", base.HEX)
field_inner_checksum = ProtoField.uint8("bisecur.innerChecksum", "Inner", base.HEX)
field_outer_checksum = ProtoField.uint8("bisecur.outerChecksum", "Outer", base.HEX)

-- login fields
field_user = ProtoField.string("bisecur.login.user", "Username", base.ASCII)
field_password = ProtoField.string("bisecur.login.password", "Password", base.ASCII)

bisecur_protocol.fields = { sender, receiver, body_length, tag, token, command, payload, response, field_inner_checksum, field_outer_checksum, field_user, field_password }

function bisecur_protocol.dissector(buffer, pinfo, tree)
  length = buffer:len()
  if length == 0 then return end

  pinfo.cols.protocol = "BiSecur" -- bisecur_protocol.name

  local command_id = tonumber(tostring(UInt64.fromhex(buffer(38, 2):string())))
  local command_name = get_command_name(command_id)
  local is_response = bit32.band(command_id, 128) > 0

  if is_response then
    command_id = bit.bxor(command_id, 128)
    command_name = get_command_name(command_id)

    command_name = command_name .. (" response")
  end

  local subtree = tree:add(bisecur_protocol, buffer(), "BiSecur Package, Command: " .. command_name)

  subtree:add_le(sender, buffer(0, 12))
  subtree:add_le(receiver, buffer(12, 12))

  local length = tostring(UInt64.fromhex(buffer(24, 4):string()))
  subtree:add_le(body_length, buffer(24,4), length) 

  subtree:add_le(tag, buffer(28, 2), tostring(UInt64.fromhex(buffer(28, 2):string())))
  subtree:add_le(token, buffer(30, 8))

  subtree:add_le(command, buffer(38, 2), command_id .. " (" .. command_name .. ")" )

  local inner_payload_length = buffer:len() - 40 - 2 - 2
  local inner_payload = buffer(40, inner_payload_length) -- total_length - header - inner_checksum (2 byte) - outer_checksum (2 byte)

  if command_id == 16 and is_response == false then
    -- login request
    local user_name_length = tonumber(tostring(UInt64.fromhex(buffer(40, 2):string())))
    local user_name = Struct.fromhex(buffer(42, user_name_length * 2):string())
    local user_password = Struct.fromhex(buffer(42 + user_name_length * 2, inner_payload_length - user_name_length * 2 - 2):string())

    local loginSubtree = subtree:add(bisecur_protocol, buffer(40, inner_payload_length), "Login")
    loginSubtree:add(field_user, buffer(42, user_name_length * 2), user_name)
    loginSubtree:add(field_password, buffer(42 + user_name_length * 2, inner_payload_length - user_name_length * 2 - 2), user_password)
  end

  if command_id == 6 then -- jmcp
    -- decode json
    local json = Struct.fromhex(buffer(40, inner_payload_length):string())
    subtree:add(payload, buffer(40, inner_payload_length), json)
  end

  if command_id == 38 and is_response then
    -- decode json
    local text = Struct.fromhex(buffer(40, inner_payload_length):string())
    subtree:add(response, buffer(40, inner_payload_length), text)
  end

  -- Checksums tree
  local checksumsSubtree = subtree:add(field_checksums, buffer(40 + inner_payload_length, 4), "Checksums")

  local inner_checksum_value = tostring(UInt64.fromhex(buffer(40 + inner_payload_length, 2):string()))
  checksumsSubtree:add_le(field_inner_checksum, buffer(40 + inner_payload_length, 2), inner_checksum_value);

  local outer_checksum_value = tostring(UInt64.fromhex(buffer(40 + inner_payload_length + 2, 2):string()))
  checksumsSubtree:add_le(field_outer_checksum, buffer(40 + inner_payload_length + 2, 2), outer_checksum_value)
end

function get_command_name(command_id)
    local command_name = "Unknown"
    -- print("command id = " .. command_id)
  
    if     command_id ==    0 then command_name = "PING"
    elseif command_id ==    1 then command_name = "ERROR"
    elseif command_id ==    2 then command_name = "GET_MAC"
    elseif command_id ==    3 then command_name = "SET_VALUE"
    elseif command_id ==    6 then command_name = "JMCP"
    elseif command_id ==    7 then command_name = "GET_GW_VERSION"
    elseif command_id ==   16 then command_name = "LOGIN"
    elseif command_id ==   17 then command_name = "LOGOUT"
    elseif command_id ==   34 then command_name = "ADD_USER"
    elseif command_id ==   37 then command_name = "SET_USER_RIGHTS"
    elseif command_id ==   38 then command_name = "GET_NAME"
    elseif command_id ==   51 then command_name = "SET_STATE"
    elseif command_id ==   52 then command_name = "GET_PORT_NAME"
    elseif command_id ==   53 then command_name = "SET_PORT_NAME"
    elseif command_id ==   81 then command_name = "SCAN_WIFI"
    elseif command_id ==   82 then command_name = "WIFI_FOUND"
    elseif command_id ==   83 then command_name = "GET_WIFI_STATE"
    elseif command_id ==  112 then command_name = "HM_GET_TRANSITION"
    end
  
    return command_name
  end

-- register for a tcp/4000
local tcp_port = DissectorTable.get("tcp.port")
tcp_port:add(4000, bisecur_protocol)
