using Liquip.API.Attributes;

namespace Liquip.NativeWrapper
{
    public class NativeWrapperObjectPlug
    {
        [Plug(typeof(NativeWrapperObject))]
        public class AThisObjectPlug
        {
            public static void Speak(object aThis)
            {
                Console.WriteLine("bz bz plugged hello");
            }

            public static int InstanceMethod(object aThis, int value)
            {
                var obj = (NativeWrapperObjectPlug)aThis;

                return value * 2;
            }
        }
    }
}
