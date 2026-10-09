// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Reflection;
using System.Windows.Data;

namespace Wpf.Ui.Controls;

public partial class TabView
{
    // Synchronous CLR bindings only: arbitrary converters and validation cannot be rolled back safely.
    private sealed class PropertyWrite(
        DependencyObject target,
        DependencyProperty dp,
        BindingExpression? binding,
        PropertyInfo? property
    )
    {
        internal static PropertyWrite? TryCreate(
            DependencyObject target,
            DependencyProperty dp,
            bool allowLocal
        )
        {
            try
            {
                return Prepare(target, dp, allowLocal);
            }
            catch
            {
                // A model getter can reject access during capability checks.
                return null;
            }
        }

        private static PropertyWrite? Prepare(DependencyObject target, DependencyProperty dp, bool allowLocal)
        {
            if (target is Freezable { IsFrozen: true })
            {
                return null;
            }

            BindingExpression? expression = BindingOperations.GetBindingExpression(target, dp);
            if (!BindingOperations.IsDataBound(target, dp))
            {
                return allowLocal && !DependencyPropertyHelper.GetValueSource(target, dp).IsExpression
                    ? new(target, dp, null, null)
                    : null;
            }

            if (expression == null || expression.Status != BindingStatus.Active || expression.HasError)
            {
                return null;
            }

            Binding binding = expression.ParentBinding;
            object? source = expression.ResolvedSource;
            PropertyInfo? property = source
                ?.GetType()
                .GetProperty(expression.ResolvedSourcePropertyName ?? string.Empty);
            if (
                binding.Mode != BindingMode.TwoWay
                || binding.IsAsync
                || binding.Delay != 0
                || binding.Converter != null
                || binding.ValidationRules.Count != 0
                || (binding.ValidatesOnDataErrors && source is System.ComponentModel.IDataErrorInfo)
                || (
                    binding.ValidatesOnNotifyDataErrors
                    && source is System.ComponentModel.INotifyDataErrorInfo
                )
                || !string.IsNullOrEmpty(binding.BindingGroupName)
                || source == null
                || source.GetType().IsValueType
                || property?.PropertyType != dp.PropertyType
                || property.GetIndexParameters().Length != 0
                || property.GetMethod?.IsPublic != true
                || property.SetMethod?.IsPublic != true
                || property
                    .SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit")
                || !Equals(property.GetValue(source), target.GetValue(dp))
            )
            {
                return null;
            }

            return new(target, dp, expression, property);
        }

        internal bool Set(object? value)
        {
            if (
                target is Freezable { IsFrozen: true }
                || (binding != null && BindingOperations.GetBindingExpression(target, dp) != binding)
            )
            {
                return false;
            }

            target.SetCurrentValue(dp, value);
            if (
                binding != null
                && (binding.HasError || !Equals(property!.GetValue(binding.ResolvedSource), value))
            )
            {
                binding.UpdateSource();
            }

            return Equals(target.GetValue(dp), value)
                && (
                    binding == null
                    || (!binding.HasError && Equals(property!.GetValue(binding.ResolvedSource), value))
                );
        }
    }
}
