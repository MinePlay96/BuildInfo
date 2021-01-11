﻿using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Digi.BuildInfo
{
    public class Constants : ModComponent
    {
        public const int MOD_VERSION = 3; // notifies player of notable changes and links them to workshop's changelog.

        public readonly Vector2 BLOCKINFO_SIZE = new Vector2(0.02164f, 0.00076f);
        public const float ASPECT_RATIO_54_FIX = 0.938f;
        public const float BLOCKINFO_TEXT_PADDING = 0.001f;

        public const int TICKS_PER_SECOND = 60;

        public readonly MyDefinitionId COMPUTER_COMPONENT_ID = new MyDefinitionId(typeof(MyObjectBuilder_Component), MyStringHash.GetOrCompute("Computer")); // HACK: this is what the game uses for determining if a block can have ownership

        public const bool BLOCKPICKER_IN_MP = true;
        public const string BLOCKPICKER_DISABLED_CONFIG = "NOTE: This feature is disabled in MP because of issues, see: https://support.keenswh.com/spaceengineers/general/topic/187-2-modapi-settoolbarslottoitem-causes-everyone-in-server-to-disconnect";
        public const string BLOCKPICKER_DISABLED_CHAT = "Pick block feature disabled in MP because of issues, see workshop page for details.";

        public static bool EXPORT_VANILLA_BLOCKS = false; // used for exporting vanilla block IDs for AnalyseShip's hardcoded list.

        public readonly HashSet<MyObjectBuilderType> DEFAULT_ALLOWED_TYPES = new HashSet<MyObjectBuilderType>(MyObjectBuilderType.Comparer) // used in inventory formatting if type argument is null
        {
            typeof(MyObjectBuilder_Ore),
            typeof(MyObjectBuilder_Ingot),
            typeof(MyObjectBuilder_Component)
        };

        public readonly MyStringId[] CONTROL_SLOTS = new MyStringId[]
        {
            MyControlsSpace.SLOT0,
            MyControlsSpace.SLOT1,
            MyControlsSpace.SLOT2,
            MyControlsSpace.SLOT3,
            MyControlsSpace.SLOT4,
            MyControlsSpace.SLOT5,
            MyControlsSpace.SLOT6,
            MyControlsSpace.SLOT7,
            MyControlsSpace.SLOT8,
            MyControlsSpace.SLOT9,
        };

        public Constants(BuildInfoMod main) : base(main)
        {
            ComputeResourceGroups();
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        #region Resource group priorities
        public int resourceSinkGroups = 0;
        public int resourceSourceGroups = 0;
        public readonly Dictionary<MyStringHash, ResourceGroupData> resourceGroupPriority
                  = new Dictionary<MyStringHash, ResourceGroupData>(MyStringHash.Comparer);

        private void ComputeResourceGroups()
        {
            resourceGroupPriority.Clear();
            resourceSourceGroups = 0;
            resourceSinkGroups = 0;

            // from MyResourceDistributorComponent.InitializeMappings()
            var groupDefs = MyDefinitionManager.Static.GetDefinitionsOfType<MyResourceDistributionGroupDefinition>();
            var orderedGroupsEnumerable = groupDefs.OrderBy((def) => def.Priority);

            // compact priorities into an ordered number.
            foreach(var group in orderedGroupsEnumerable)
            {
                int priority = 0;

                if(group.IsSource)
                {
                    resourceSourceGroups++;
                    priority = resourceSourceGroups;
                }
                else
                {
                    resourceSinkGroups++;
                    priority = resourceSinkGroups;
                }

                resourceGroupPriority.Add(group.Id.SubtypeId, new ResourceGroupData(group, priority));
            }
        }

        public struct ResourceGroupData
        {
            public readonly MyResourceDistributionGroupDefinition Def;
            public readonly int Priority;

            public ResourceGroupData(MyResourceDistributionGroupDefinition def, int priority)
            {
                Def = def;
                Priority = priority;
            }
        }
        #endregion Resource group priorities
    }
}
