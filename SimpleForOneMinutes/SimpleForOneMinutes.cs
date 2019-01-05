using System;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSLabScripts
{
    public class SimpleForOneMinutes : SimpleCommon, IExternalScript
    {        
        protected override int DataInterval => 1;
        protected override TimeSpan TimeBeginBar => new TimeSpan(10, 04, 55);
        protected override TimeSpan TimeOneBar => new TimeSpan(0, 1, 0);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }
    }
}
