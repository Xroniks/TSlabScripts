using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TSlabScripts
{
    public class TradingModel
    {
        public double Value { get; set; }

        public double EnterPrice { get; set; }

        public double StopPrice { get; set; }

        public double ProfitPrice { get; set; }

        public string GetNamePosition => Value + "_" + EnterPrice + "_" + StopPrice + "_" + ProfitPrice;
    }
}
