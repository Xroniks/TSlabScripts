using System;
using Simple;
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
        public OptimProperty MultyplayDivider = new OptimProperty(10, 1.0, 1000, 10);
        public OptimProperty PriceStep = new OptimProperty(10, 0.001, 100, double.MaxValue);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }

        protected override TradingModel GetNewLongTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value - CalculatePrice(bc, MultyplayDelta),
                StopPrice = IsReverseMode ? value + CalculatePrice(bc, MultyplayStop) : value - CalculatePrice(bc, MultyplayStop),
                ProfitPrice = IsReverseMode ? value - CalculatePrice(bc, MultyplayProfit) : value + CalculatePrice(bc, MultyplayProfit)
            };
        }

        protected override TradingModel GetNewShortTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + CalculatePrice(bc, MultyplayDelta),
                StopPrice = IsReverseMode ? value - CalculatePrice(bc, MultyplayStop) : value + CalculatePrice(bc, MultyplayStop),
                ProfitPrice = IsReverseMode ? value + CalculatePrice(bc, MultyplayProfit) : value - CalculatePrice(bc, MultyplayProfit)
            };
        }

        protected double CalculatePrice(double bc, double baseLogarithm)
        {
            return Math.Round(Math.Log(bc / MultyplayDivider, baseLogarithm) / PriceStep, 0) * PriceStep;
        }
    }
}