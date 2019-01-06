using System;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSLabScripts
{
    public class SimpleForFiveMinutes : SimpleCommon, IExternalScript
    {       
        protected override int DataInterval => 5;
        protected override TimeSpan TimeBeginBar => new TimeSpan(10, 04, 55);
        protected override TimeSpan TimeOneBar => new TimeSpan(0, 5, 0);

        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }
    }
}
