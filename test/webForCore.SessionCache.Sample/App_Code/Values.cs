using System;

namespace SessionCacheSample
{
    // A value that goes through BinaryFormatter on the way to the store.
    [Serializable]
    public class Basket
    {
        public string Owner;
        public int Items;
    }

    // Not [Serializable]: out-of-process session state can't keep it (#180).
    public class NotSerializableValue
    {
        public string Text;
    }
}
