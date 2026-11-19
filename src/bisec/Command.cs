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
    /// Codes and names mirror the legacy app's MCPCommands table.
    /// </summary>
    public class Command
    {
        public static readonly Command Empty = new Command(-1, "EMPTY", false);
        public static readonly Command Ping = new Command(0, "PING", false);
        public static readonly Command Error = new Command(1, "ERROR", false);
        public static readonly Command GetMac = new Command(2, "GET_MAC", false);
        public static readonly Command SetValue = new Command(3, "SET_VALUE");
        public static readonly Command GetValue = new Command(4, "GET_VALUE");
        public static readonly Command Debug = new Command(5, "DEBUG");
        public static readonly Command Jmcp = new Command(6, "JMCP");
        public static readonly Command GetGatewayVersion = new Command(7, "GET_GW_VERSION", false);
        public static readonly Command Login = new Command(16, "LOGIN", false);
        public static readonly Command Logout = new Command(17, "LOGOUT");
        public static readonly Command GetUserIds = new Command(32, "GET_USER_IDS");
        public static readonly Command GetUserName = new Command(33, "GET_USER_NAME");
        public static readonly Command AddUser = new Command(34, "ADD_USER");
        public static readonly Command ChangePassword = new Command(35, "CHANGE_PASSWD");
        public static readonly Command RemoveUser = new Command(36, "REMOVE_USER");
        public static readonly Command SetUserRights = new Command(37, "SET_USER_RIGHTS");
        public static readonly Command GetName = new Command(38, "GET_NAME", false);
        public static readonly Command SetName = new Command(39, "SET_NAME");
        public static readonly Command GetUserRights = new Command(40, "GET_USER_RIGHTS");
        public static readonly Command AddPort = new Command(41, "ADD_PORT");
        public static readonly Command AddGroup = new Command(42, "ADD_GROUP");
        public static readonly Command RemoveGroup = new Command(43, "REMOVE_GROUP");
        public static readonly Command SetGroupName = new Command(44, "SET_GROUP_NAME");
        public static readonly Command GetGroupName = new Command(45, "GET_GROUP_NAME");
        public static readonly Command SetGroupedPorts = new Command(46, "SET_GROUPED_PORTS");
        public static readonly Command GetGroupedPorts = new Command(47, "GET_GROUPED_PORTS");
        public static readonly Command GetPorts = new Command(48, "GET_PORTS");
        public static readonly Command GetPortType = new Command(49, "GET_TYPE");
        public static readonly Command GetState = new Command(50, "GET_STATE");
        public static readonly Command SetState = new Command(51, "SET_STATE");
        public static readonly Command GetPortName = new Command(52, "GET_PORT_NAME");
        public static readonly Command SetPortName = new Command(53, "SET_PORT_NAME");
        public static readonly Command SetPortType = new Command(54, "SET_TYPE");
        public static readonly Command GetGroupIds = new Command(64, "GET_GROUP_IDS");
        public static readonly Command InheritPort = new Command(65, "INHERIT_PORT");
        public static readonly Command RemovePort = new Command(66, "REMOVE_PORT");
        public static readonly Command ChangeUserName = new Command(67, "CHANGE_USER_NAME");
        public static readonly Command ChangeUserNameOfUser = new Command(68, "CHANGE_USER_NAME_OF_USER");
        public static readonly Command ChangePasswordOfUser = new Command(69, "CHANGE_PASSWORD_OF_USER");
        public static readonly Command SetSsl = new Command(80, "SET_SSL");
        public static readonly Command ScanWifi = new Command(81, "SCAN_WIFI");
        public static readonly Command WifiFound = new Command(82, "WIFI_FOUND");
        public static readonly Command GetWifiState = new Command(83, "GET_WIFI_STATE");
        public static readonly Command GetTransition = new Command(112, "HM_GET_TRANSITION");

        public static readonly Command[] Commands = new Command[]
        {
            Empty,
            Ping,
            Error,
            GetMac,
            SetValue,
            GetValue,
            Debug,
            Jmcp,
            GetGatewayVersion,
            Login,
            Logout,
            GetUserIds,
            GetUserName,
            AddUser,
            ChangePassword,
            RemoveUser,
            SetUserRights,
            GetName,
            SetName,
            GetUserRights,
            AddPort,
            AddGroup,
            RemoveGroup,
            SetGroupName,
            GetGroupName,
            SetGroupedPorts,
            GetGroupedPorts,
            GetPorts,
            GetPortType,
            GetState,
            SetState,
            GetPortName,
            SetPortName,
            SetPortType,
            GetGroupIds,
            InheritPort,
            RemovePort,
            ChangeUserName,
            ChangeUserNameOfUser,
            ChangePasswordOfUser,
            SetSsl,
            ScanWifi,
            WifiFound,
            GetWifiState,
            GetTransition,
        };

        public int Code { get; }
        public string Name { get; }
        public bool AuthenticationRequired { get; }

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
