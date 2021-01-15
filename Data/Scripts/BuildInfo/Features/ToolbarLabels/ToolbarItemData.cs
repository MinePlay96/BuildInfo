﻿using Sandbox.Common.ObjectBuilders;

namespace Digi.BuildInfo.Features.ToolbarLabels
{
    public struct ToolbarItemData
    {
        public readonly int Index;
        public readonly string ActionId;
        public readonly string LabelWrapped;
        public readonly string GroupName;
        public readonly string GroupNameWrapped;
        public readonly string PBRunArgumentWrapped;

        public ToolbarItemData(int index, string actionId, string label, string group, MyObjectBuilder_ToolbarItemTerminal blockItem)
        {
            Index = index;
            ActionId = actionId;
            LabelWrapped = GetWrappedText(label);
            GroupName = group;
            GroupNameWrapped = GetWrappedText(group);
            PBRunArgumentWrapped = null;

            // HACK major assumptions here, but there's no other use case and some stuff is prohibited so just w/e
            if(blockItem?.Parameters != null && blockItem.Parameters.Count > 0 && blockItem._Action == "Run")
            {
                string arg = blockItem.Parameters[0]?.Value;
                if(arg != null)
                {
                    PBRunArgumentWrapped = GetWrappedText(arg, ToolbarCustomNames.CustomLabelMaxLength);
                }
            }
        }

        private static string GetWrappedText(string text, int maxLength = ToolbarCustomNames.CustomLabelMaxLength)
        {
            if(text == null)
                return null;

            if(text == string.Empty)
                return string.Empty;

            var sb = BuildInfoMod.Instance.Caches.WordWrapTempSB;
            sb.Clear();
            ToolbarActionLabels.AppendWordWrapped(sb, text, maxLength);
            return sb.ToString();
        }
    }
}