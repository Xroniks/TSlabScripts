using System;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSLabScripts
{
    public class SimpleForOneMinutes : SimpleCommon, IExternalScript
    {        
        public new int DataInterval => 1;
        public new TimeSpan TimeBeginBar => new TimeSpan(10, 00, 55);
        public new TimeSpan TimeOneBar => new TimeSpan(0, 1, 0);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }
    }
}
