using System;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class SimpleForOneMinutes : SimpleCommon
    {        
        protected override int DataInterval => 1;
        protected override TimeSpan TimeBeginBar => new TimeSpan(10, 04, 55);
        protected override TimeSpan TimeOneBar => new TimeSpan(0, 1, 0);
    }
}
