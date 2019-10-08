using System;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class SimpleForFiveMinutesAndParabolicSar : SimpleCommon, IExternalScript
    {
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
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

            return new Indicators { Parabolic = parabolic };
        }

        protected override void SetShortStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            if (IsReverseMode)
            {
                base.SetShortStop(actualBar, position, arr, indicators);
                return;
            }
            
            var value = Convert.ToDouble(indicators.Parabolic[actualBar]);
            position.CloseAtStop(actualBar + 1, value, Slippage, "closeStop");
        }

        protected override void SetLongStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            if (IsReverseMode)
            {
                base.SetLongStop(actualBar, position, arr, indicators);
                return;
            }
            
            var value = Convert.ToDouble(indicators.Parabolic[actualBar]);
            position.CloseAtStop(actualBar + 1, value, Slippage, "closeStop");
        }
        public override void CreateSellOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var parabolicValue = indicators.Parabolic[actualBar];
            if (parabolicValue > model.EnterPrice)
            {
                base.CreateSellOrder(source, actualBar, model, indicators);
            }
        }
        
        public override void CreateBuyOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var parabolicValue = indicators.Parabolic[actualBar];
            if (model.EnterPrice > parabolicValue)
            {
                base.CreateBuyOrder(source, actualBar, model, indicators);
            }
        }
    }
}
