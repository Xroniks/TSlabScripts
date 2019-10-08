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
        public OptimProperty MultyplayDivider = new OptimProperty(10, 1.0, 1000, 10);
        public OptimProperty PriceStep = new OptimProperty(10, 0.001, 100, double.MaxValue);
        
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public OptimProperty PeriodEMA = new OptimProperty(34, 1, 1000, 0.01);
        
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
                StopPrice = value - CalculatePrice(bc, MultyplayStop),
                ProfitPrice = value + CalculatePrice(bc, MultyplayProfit)
            };
        }

        protected override TradingModel GetNewShortTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + CalculatePrice(bc, MultyplayDelta),
                StopPrice = value + CalculatePrice(bc, MultyplayStop),
                ProfitPrice = value - CalculatePrice(bc, MultyplayProfit)
            };
        }

        protected double CalculatePrice(double bc, double baseLogarithm)
        {
            return Math.Round(Math.Log(bc / MultyplayDivider, baseLogarithm) / PriceStep, 0) * PriceStep;
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
            var parabolicStop = Convert.ToDouble(indicators.Parabolic[actualBar]);
            var modelStop = Convert.ToDouble(arr[3]);
            position.CloseAtStop(actualBar + 1, Math.Min(parabolicStop, modelStop), Slippage, "closeStop");
        }

        protected override void SetBuyStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var parabolicStop = Convert.ToDouble(indicators.Parabolic[actualBar]);
            var modelStop = Convert.ToDouble(arr[3]);
            position.CloseAtStop(actualBar + 1, Math.Max(parabolicStop, modelStop), Slippage, "closeStop");
        }
        
        public override void CreateSellOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var emaValue = indicators.EMA[actualBar];
            var parabolicValue = indicators.Parabolic[actualBar];
            if (emaValue > model.EnterPrice && parabolicValue > model.EnterPrice)
            {
                source.Positions.SellIfLess(actualBar + 1, Value, model.EnterPrice, Slippage,"sell_" + model.GetNamePosition);
            }
        }
        
        public override void CreateBuyOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var emaValue = indicators.EMA[actualBar];
            var parabolicValue = indicators.Parabolic[actualBar];
            if (model.EnterPrice > emaValue && model.EnterPrice > parabolicValue)
            {
                source.Positions.BuyIfGreater(actualBar + 1, Value, model.EnterPrice, Slippage,"buy_" + model.GetNamePosition);
            }
        }
    }
}