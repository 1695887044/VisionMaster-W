// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

global using G.Services.Common;
using G.Services.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace G.Controls.Chart2D
{
    [Display(Name = "柱状图")]
    public class BarPresenter : LinePresenter
    {
        public BarPresenter()
        {

        }
        public BarPresenter(IEnumerable<double> data) : base(data)
        {

        }

        public BarPresenter(IChartDataProvider dataProvider) : base(dataProvider)
        {

        }
    }
}

