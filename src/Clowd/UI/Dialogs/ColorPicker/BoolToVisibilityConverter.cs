﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="BoolToVisibilityConverter.cs" company="PropertyTools">
//   Copyright (c) 2014 PropertyTools contributors
// </copyright>
// <summary>
//   Converts bool instances to Visibility instances.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Clowd.UI.Dialogs.ColorPicker
{
    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;

    /// <summary>
    /// Converts <see cref="bool" /> instances to <see cref="Visibility" /> instances.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoolToVisibilityConverter" /> class.
        /// </summary>
        public BoolToVisibilityConverter()
        {
            this.InvertVisibility = false;
            this.NotVisibleValue = Visibility.Collapsed;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to invert visibility.
        /// </summary>
        public bool InvertVisibility { get; set; }

        /// <summary>
        /// Gets or sets the not visible value.
        /// </summary>
        /// <value>The not visible value.</value>
        public Visibility NotVisibleValue { get; set; }

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
            if (value == null)
            {
                return Visibility.Visible;
            }

            bool visible = true;
            if (value is bool)
            {
                visible = (bool)value;
            }

            if (this.InvertVisibility)
            {
                visible = !visible;
            }

            return visible ? Visibility.Visible : this.NotVisibleValue;
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
            return ((value is Visibility) && (((Visibility)value) == Visibility.Visible))
                ? !this.InvertVisibility
                : this.InvertVisibility;
        }
    }
}
