﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HueToColorConverter.cs" company="PropertyTools">
//   Copyright (c) 2014 PropertyTools contributors
// </copyright>
// <summary>
//   Converts a hue values to Color instances.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Clowd.UI.Dialogs.ColorPicker
{
    using System;
    using System.Globalization;
    using System.Windows.Data;
    using System.Windows.Media;

    /// <summary>
    /// Converts a hue values to <see cref="Color" /> instances.
    /// </summary>
    [ValueConversion(typeof(double), typeof(Color))]
    public class HueToColorConverter : IValueConverter
    {
        /// <summary>
        /// Converts a value.
        /// </summary>
        /// <param name="value">The value produced by the binding source.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">The converter parameter to use.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>
        /// A converted value. If the method returns <c>null</c>, the valid <c>null</c> value is used.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var doubleValue = (double)value;

            return ColorHelper.HsvToColor(doubleValue / 360, 1, 1);
        }

        /// <summary>
        /// Converts a value.
        /// </summary>
        /// <param name="value">The value that is produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">The converter parameter to use.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>
        /// A converted value. If the method returns <c>null</c>, the valid <c>null</c> value is used.
        /// </returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
