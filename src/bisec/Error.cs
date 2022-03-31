namespace BiSec.Library
{
    public enum Error : byte
    {
        CommandNotFound = 0,
        InvalidProtocol = 1,
        LoginFailed = 2,
        InvalidToken =  3,
        UserAlreadyExists = 4,
        NoEmptyUserSlot = 5,
        InvalidPassword = 6,
        InvalidUsername = 7,
        UserNotFound = 8,
        PortNotFound = 9,
        PortError = 10,
        GatewayBusy = 11,
        PermissionDenied = 12,
        NoEmptyGroupSlot = 13,
        GroupNotFound = 14,
        InvalidPayload = 15,
        OutOfRange = 16,
        AddPortError = 17,
        NoEmptyPortSlot = 18,
        AdapterBusy = 19,
    }
}
