namespace BiSec.Library.Models
{
    public enum GroupType
    {
        None = 0,
        SectionalDoor = 1,
        HorizontalSectionalDoor = 2,
        SwingGateSingle = 3,
        SwingGateDouble = 4,
        SlidingGate = 5,
        Door = 6,
        Light = 7,
        Other = 8,
        Jack = 9,
        Smartkey = 10,
        PilomatPoller = 11,
        PilomatDurchfahrtssperre = 12,
        PilomatHubbalken = 13,
        Barrier = 15,
    }
}
/*
 port actions per type
         1:[PortTypes.IMPULS,PortTypes.UP,PortTypes.DOWN,PortTypes.HALF,PortTypes.LIGHT],
         2:[PortTypes.IMPULS,PortTypes.UP,PortTypes.DOWN,PortTypes.HALF,PortTypes.LIGHT],
         3:[PortTypes.IMPULS,PortTypes.HALF,PortTypes.UP,PortTypes.DOWN,PortTypes.LIGHT],
         4:[PortTypes.IMPULS,PortTypes.WALK,PortTypes.UP,PortTypes.DOWN,PortTypes.LIGHT],
         5:[PortTypes.IMPULS,PortTypes.UP,PortTypes.DOWN,PortTypes.HALF,PortTypes.LIGHT],
         6:[PortTypes.AUTO_CLOSE,PortTypes.IMPULS,PortTypes.LIGHT,PortTypes.ON_OFF],
         7:[PortTypes.ON,PortTypes.OFF,PortTypes.ON_OFF,PortTypes.IMPULS],
         8:[PortTypes.ON,PortTypes.OFF,PortTypes.ON_OFF,PortTypes.IMPULS],
         9:[PortTypes.ON,PortTypes.OFF,PortTypes.ON_OFF,PortTypes.IMPULS],
         10:[PortTypes.LOCK,PortTypes.UNLOCK,PortTypes.OPEN_DOOR],
         11:[PortTypes.LIFT,PortTypes.SINK],
         12:[PortTypes.LIFT,PortTypes.SINK],
         13:[PortTypes.LIFT,PortTypes.SINK],
         14:[PortTypes.LIFT,PortTypes.SINK],
         15:[PortTypes.IMPULS,PortTypes.UP,PortTypes.DOWN]
 */