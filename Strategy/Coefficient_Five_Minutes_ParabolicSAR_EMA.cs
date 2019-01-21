using System;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class Coefficient_Five_Minutes_ParabolicSAR_EMA : SimpleCommon, IExternalScript
    {
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public OptimProperty PeriodEMA = new OptimProperty(34, 1, 1000, 0.01);
        
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
        
        protected override Indicators AddIndicatorOnMainPain(IContext ctx, ISecurity source, IPane pain)
        {
            var parabolic = ctx.GetData("Parabolic", new[] {""}, () => new ParabolicSAR
            {
                AccelerationMax = AccelerationMax,
                AccelerationStart = AccelerationStart,
                AccelerationStep = AccelerationStep
            }.Execute(source));
            var nameParabolic = "Parabolic (" + AccelerationStart.Value + "," + AccelerationStep.Value + "," + AccelerationMax.Value + ")";
            pain.AddList(nameParabolic, parabolic, ListStyles.LINE, new Color(255, 0, 0), LineStyles.SOLID, PaneSides.RIGHT);

            var ema = ctx.GetData("EMA", new[] {""}, () => new EMA
            {
                Period = PeriodEMA
            }.Execute(source.ClosePrices));
            var nameEma = "EMA (" + PeriodEMA + ")";
            pain.AddList(nameEma, ema, ListStyles.LINE, new Color(0, 255, 0), LineStyles.SOLID, PaneSides.RIGHT);

            return new Indicators{
                Parabolic = parabolic,
                EMA = ema
            };
        }

        protected override void SetSellStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var value = Convert.ToDouble(indicators.Parabolic[actualBar]);
            position.CloseAtStop(actualBar + 1, value, Slippage, "closeStop");
        }

        protected override void SetBuyStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var value = Convert.ToDouble(indicators.Parabolic[actualBar]);
            position.CloseAtStop(actualBar + 1, value, Slippage, "closeStop");
        }
        
        public override void CreateSellOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var emaValue = indicators.EMA[actualBar];
            if (emaValue > model.EnterPrice)
            {
                source.Positions.SellIfLess(actualBar + 1, Value, model.EnterPrice, Slippage,"sell_" + model.GetNamePosition);
            }
        }
        
        public override void CreateBuyOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var emaValue = indicators.EMA[actualBar];
            if (model.EnterPrice > emaValue)
            {
                source.Positions.BuyIfGreater(actualBar + 1, Value, model.EnterPrice, Slippage,"buy_" + model.GetNamePosition);
            }
        }
    }
}