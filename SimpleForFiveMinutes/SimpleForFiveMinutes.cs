using System;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class SimpleForFiveMinutes : SimpleCommon
    {       
        protected override int DataInterval => 5;
        protected override TimeSpan TimeBeginBar => new TimeSpan(10, 04, 55);
        protected override TimeSpan TimeOneBar => new TimeSpan(0, 5, 0);
    }
}
