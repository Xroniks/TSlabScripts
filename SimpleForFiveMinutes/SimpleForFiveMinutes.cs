using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSLabScripts
{
    public class SimpleForFiveMinutes : SimpleCommon, IExternalScript
    {       
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }
    }
}
