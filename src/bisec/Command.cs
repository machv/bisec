using System;
using System.Linq;

namespace BiSec.Library
{
    /*
    [AttributeUsage(AttributeTargets.Field)]
    public class BiSecurCommandAttribute : Attribute
    {
        public bool AuthenticationRequired { get; set; }
    }

    public enum CommandId
    {
        Empty = -1,
        Ping = 0,
        Error = 1,
        GetMac = 2,
        [BiSecurCommand(AuthenticationRequired = true)]
        SetValue = 3,
        Jmcp = 6, 
        GetGatewayVersion = 7,
        Login = 16,
        Logout = 17,
        GetName = 38,
        SetState = 51, 
        GetTransition = 112,
        GetWifiState = 83,
        ScanWifi = 81, 
        WifiFound = 82,
        GetPortName = 52,
        SetPortName = 53,
        AddUser = 34,
        SetUserRights = 37,
    }
    */
    /// <summary>
    /// The protocol between the client and the bisecure gateway uses a 1-byte command to signal whats to be done.
    /// In the result from the GW the command has the 7-th bit set to signal a response.
    /// </summary>
    public class Command
    {
        public static readonly Command Empty = new Command(-1, "EMPTY", false);
        public static readonly Command Ping = new Command(0, "PING", false);
        public static readonly Command Error = new Command(1, "ERROR", false);
        public static readonly Command GetMac = new Command(2, "GET_MAC", false);
        public static readonly Command SetValue = new Command(3, "SET_VALUE");
        public static readonly Command Jmcp = new Command(6, "JMCP");
        public static readonly Command GetGatewayVersion = new Command(7, "GET_GW_VERSION", false);
        public static readonly Command Login = new Command(16, "LOGIN", false);
        public static readonly Command Logout = new Command(17, "LOGOUT");
        public static readonly Command GetName = new Command(38, "GET_NAME", false);
        public static readonly Command SetState = new Command(51, "SET_STATE");
        public static readonly Command GetTransition = new Command(112, "HM_GET_TRANSITION");
        public static readonly Command GET_WIFI_STATE = new Command(83, "GET_WIFI_STATE");
        public static readonly Command SCAN_WIFI = new Command(81, "SCAN_WIFI");
        public static readonly Command WIFI_FOUND = new Command(82, "WIFI_FOUND");
        public static readonly Command GET_PORT_NAME = new Command(52, "GET_PORT_NAME");
        public static readonly Command SET_PORT_NAME = new Command(53, "SET_PORT_NAME");
        public static readonly Command ADD_USER = new Command(34, "ADD_USER");
        public static readonly Command SET_USER_RIGHTS = new Command(37, "SET_USER_RIGHTS");

        public static Command[] Commands = new Command[]
        {
            Empty,
            Ping,
            Error,
            GetMac,
            SetValue,
            Jmcp,
            GetGatewayVersion,
            Login,
            Logout,
            GetName,
            SetState,
            GetTransition,
            GET_WIFI_STATE,
            SCAN_WIFI,
            WIFI_FOUND,
            GET_PORT_NAME,
            SET_PORT_NAME,
            ADD_USER,
            SET_USER_RIGHTS,
        };

        public int Code { get; set; }
        public string Name { get; set; }
        public bool AuthenticationRequired { get; set; }

        public Command(int code, string name = "UNKNOWN", bool authenticationRequired = true)
        {
            Code = code;
            Name = name;
            AuthenticationRequired = authenticationRequired;
        }

        public static Command GetCommand(int code)
        {
            return Commands.Where(c => c.Code == code).FirstOrDefault() ?? new Command(code);
        }

        public override string ToString()
        {
            return $"Command(code = {Code:X4}, name = {Name})";
        }
    }
}
