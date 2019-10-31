﻿using System;
using Simple;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class CoefficientForFiveMinutesAndParabolicSar : SimpleCommon, IExternalScript
    {
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayDivider = new OptimProperty(10, 1.0, 1000, 10);
        public OptimProperty PriceStep = new OptimProperty(10, 0.001, 100, double.MaxValue);
        
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public void Execute(IContext ctx, ISecurity source)
        {
            BaseExecute(ctx, source);
        }

        protected override TradingModel GetNewLongTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value - CalculatePrice(bc, MultyplayDelta) - ExtraDelta,
                StopPrice = value - CalculatePrice(bc, MultyplayStop),
                ProfitPrice = value + CalculatePrice(bc, MultyplayProfit)
            };
        }

        protected override TradingModel GetNewShortTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + CalculatePrice(bc, MultyplayDelta) + ExtraDelta,
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

            return new Indicators { Parabolic = parabolic };
        }

        protected override void SetShortStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var parabolicStop = Convert.ToDouble(indicators.Parabolic[actualBar]);
            var modelStop = Convert.ToDouble(arr[3]);
            position.CloseAtStop(actualBar + 1, Math.Min(parabolicStop, modelStop), Slippage, "closeStop");
        }

        protected override void SetLongStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var parabolicStop = Convert.ToDouble(indicators.Parabolic[actualBar]);
            var modelStop = Convert.ToDouble(arr[3]);
            position.CloseAtStop(actualBar + 1, Math.Max(parabolicStop, modelStop), Slippage, "closeStop");
        }
        
        protected override void SetLongProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            base.SetLongProfit(actualBar, position, arr, indicators);
        }
        
        protected override void SetShortProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            base.SetShortProfit(actualBar, position, arr, indicators);
        }
        
        public override void CreateShortOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var parabolicValue = indicators.Parabolic[actualBar];

            var isCreate = parabolicValue > model.EnterPrice;
            if (isCreate)
            {
                base.CreateShortOrder(source, actualBar, model, indicators);
            }
        }
        
        public override void CreateLongOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            var parabolicValue = indicators.Parabolic[actualBar];

            var isCreate = model.EnterPrice > parabolicValue;
            if (isCreate)
            {
                base.CreateLongOrder(source, actualBar, model, indicators);
            }
        }
    }
}