using MrmLib;

namespace MrmTool.Common
{
    internal static class QualifierDisplay
    {
        private static string L(string key) => LocalizationService.GetString(key);

        internal static string AttributeName(QualifierAttribute a) => a switch
        {
            QualifierAttribute.Language => L("Qualifier.Attribute.Language"),
            QualifierAttribute.Contrast => L("Qualifier.Attribute.Contrast"),
            QualifierAttribute.Scale => L("Qualifier.Attribute.Scale"),
            QualifierAttribute.HomeRegion => L("Qualifier.Attribute.HomeRegion"),
            QualifierAttribute.TargetSize => L("Qualifier.Attribute.TargetSize"),
            QualifierAttribute.LayoutDirection => L("Qualifier.Attribute.LayoutDirection"),
            QualifierAttribute.Theme => L("Qualifier.Attribute.Theme"),
            QualifierAttribute.AlternateForm => L("Qualifier.Attribute.AlternateForm"),
            QualifierAttribute.DXFeatureLevel => L("Qualifier.Attribute.DXFeatureLevel"),
            QualifierAttribute.Configuration => L("Qualifier.Attribute.Configuration"),
            QualifierAttribute.DeviceFamily => L("Qualifier.Attribute.DeviceFamily"),
            _ => a.ToString(),
        };

        internal static string OperatorName(QualifierOperator o) => o switch
        {
            QualifierOperator.False => L("Qualifier.Operator.False"),
            QualifierOperator.True => L("Qualifier.Operator.True"),
            QualifierOperator.AttributeDefined => L("Qualifier.Operator.AttributeDefined"),
            QualifierOperator.AttributeUndefined => L("Qualifier.Operator.AttributeUndefined"),
            QualifierOperator.NotEqual => L("Qualifier.Operator.NotEqual"),
            QualifierOperator.NoMatch => L("Qualifier.Operator.NoMatch"),
            QualifierOperator.Less => L("Qualifier.Operator.Less"),
            QualifierOperator.LessOrEqual => L("Qualifier.Operator.LessOrEqual"),
            QualifierOperator.Greater => L("Qualifier.Operator.Greater"),
            QualifierOperator.GreaterOrEqual => L("Qualifier.Operator.GreaterOrEqual"),
            QualifierOperator.Match => L("Qualifier.Operator.Match"),
            QualifierOperator.Equal => L("Qualifier.Operator.Equal"),
            _ => o.ToString(),
        };

        internal static string FormatLine(Qualifier q)
        {
            var attr = AttributeName(q.Attribute);
            var op = OperatorName(q.Operator);
            var pri = q.Priority is { } p && p != 0 ? string.Format(L("Qualifier.Format.Priority"), p) : string.Empty;
            var fb = q.FallbackScore is { } s && s != 0 ? string.Format(L("Qualifier.Format.FallbackScore"), s) : string.Empty;
            return $"{attr} {op} {q.Value}{pri}{fb}";
        }
    }
}
