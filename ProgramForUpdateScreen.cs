using System;

namespace WXRobot
{
    public partial class Program
    {
        private static DateTime UpdateScreenAndWait(DateTime startDT, int timesForException, bool wait1Second)
        {
            var currentDT = UpdateScreenInfo(startDT, true, timesForException);

            if (wait1Second)
            {
                //每隔一秒，更新一次屏幕
                Common.Time.WaitFor.wait(new TimeSpan(0, 0, 1));
            }

            return currentDT;
        }
    }
}
