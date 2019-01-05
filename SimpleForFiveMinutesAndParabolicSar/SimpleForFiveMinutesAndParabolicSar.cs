﻿using System;
using System.Collections.Generic;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class SimpleForFiveMinutesAndParabolicSar : SimpleForFiveMinutes
    {
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);

        protected override IList<double> AddIndicatorOnMainPain(IContext ctx, ISecurity source, IPane pain)
        {
            var parabolic = ctx.GetData("Parabolic", new[] {""}, () => new ParabolicSAR
            {
                AccelerationMax = AccelerationMax,
                AccelerationStart = AccelerationStart,
                AccelerationStep = AccelerationStep
            }.Execute(source));
            var nameParabolic = "Parabolic (" + AccelerationStart.Value + "," + AccelerationStep.Value + "," +
                                AccelerationMax.Value + ")";
            pain.AddList(nameParabolic, parabolic, ListStyles.LINE, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

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
