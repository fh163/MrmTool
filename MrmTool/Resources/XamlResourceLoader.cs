using Windows.UI.Xaml.Resources;

namespace MrmTool.Resources
{
    internal partial class XamlResourceLoader : CustomXamlResourceLoader
    {
        protected override object GetResource(string resourceId, string objectType, string propertyName, string propertyType)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return string.Empty;
            }

            try
            {
                var localValue = Common.LocalizationService.GetString(resourceId);
                if (!string.Equals(localValue, resourceId, StringComparison.Ordinal))
                {
                    return localValue;
                }
            }
            catch
            {
                // Fall through to PRI map and eventually resource id text.
            }

            if (Program._resourceMap is null)
            {
                return resourceId;
            }

            try
            {
                return Program._resourceMap[$"Strings/{resourceId}"].Resolve().ValueAsString;
            }
            catch (Exception ex)
            {
                return resourceId;
            }
        }
    }
}
