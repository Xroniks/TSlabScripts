using System;
using System.Collections.Generic;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace CoefficientForFiveMinutesAndParabolicSar
{
    public class CoefficientForFiveMinutesAndParabolicSar : SimpleCommon, IExternalScript
    {
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
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
        
        protected override IList<double> AddIndicatorOnMainPain(IContext ctx, ISecurity source, IPane pain)
        {
            var parabolic = ctx.GetData("Parabolic", new[] {""}, () => new ParabolicSAR
            {
                AccelerationMax = AccelerationMax,
                AccelerationStart = AccelerationStart,
                AccelerationStep = AccelerationStep
            }.Execute(source));
            var nameParabolic = "Parabolic (" + AccelerationStart.Value + "," + AccelerationStep.Value + "," + AccelerationMax.Value + ")";
            pain.AddList(nameParabolic, parabolic, ListStyles.LINE, new Color(255, 0, 0), LineStyles.SOLID, PaneSides.RIGHT);

            return parabolic;
        }

        protected override void SetSellStop(int actualBar, IPosition position, string[] arr, IList<double> indicator)
        {
            position.CloseAtStop(actualBar + 1, Convert.ToDouble(indicator[actualBar]), Slippage, "closeStop");
        }

        protected override void SetBuyStop(int actualBar, IPosition position, string[] arr, IList<double> indicator)
        {
            position.CloseAtStop(actualBar + 1, Convert.ToDouble(indicator[actualBar]), Slippage, "closeStop");
        }
    }
}