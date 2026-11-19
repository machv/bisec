namespace BiSec.Library
{
    // Port of APK refactor.bisecur._2_SAL.stateHelper.ActorState. The app reuses the same
    // five values for other group kinds: Closed is also "on" and "extended", Open is also
    // "off" and "retracted", Half2 is also "not retracted".
    public enum ActorState
    {
        Closed,
        Half3,
        Half2,
        Half1,
        Open,
    }
}
