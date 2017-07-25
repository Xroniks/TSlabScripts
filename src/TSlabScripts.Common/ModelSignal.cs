using System.Collections.Generic;

namespace TSlabScripts.Common
{
    public class ModelSignal
    {
        public ModelSignal(int count)
        {
            BuySignal = new List<double>();
            SellSignal = new List<double>();
            for (int i = 0; i < count; i++)
            {
                BuySignal.Add(0);
                SellSignal.Add(0);
            }
        }

        public IList<double> BuySignal { get; set; }

        public IList<double> SellSignal { get; set; }
    }
}
