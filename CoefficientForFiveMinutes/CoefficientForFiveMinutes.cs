using System;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class CoefficientForFiveMinutes : SimpleCommon, IExternalScript
    {
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        
        protected override int DataInterval => 5;
        protected override TimeSpan TimeBeginBar => new TimeSpan(10, 04, 55);
        protected override TimeSpan TimeOneBar => new TimeSpan(0, 5, 0);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }

        protected override TradingModel GetNewBuyTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayDelta) / 10.0, 0) * 10.0,
                StopPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayStop) / 10.0, 0) * 10.0,
                ProfitPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayProfit) / 10.0, 0) * 10.0
            };
        }

        protected override TradingModel GetNewSellTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayDelta) / 10.0, 0) * 10.0,
                StopPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayStop) / 10.0, 0) * 10.0,
                ProfitPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayProfit) / 10.0, 0) * 10.0
            };
        }
    }
}