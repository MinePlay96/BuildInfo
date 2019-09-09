﻿using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.BuildInfo.Utils
{
    public class Caches : ModComponent
    {
        public readonly Dictionary<string, IMyModelDummy> Dummies = new Dictionary<string, IMyModelDummy>();
        public readonly HashSet<Vector3I> Vector3ISet = new HashSet<Vector3I>(Vector3I.Comparer);
        public readonly HashSet<MyObjectBuilderType> OBTypeSet = new HashSet<MyObjectBuilderType>(MyObjectBuilderType.Comparer);
        public readonly HashSet<MyDefinitionId> DefIdSet = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);

        public Caches(BuildInfoMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        public static HashSet<MyObjectBuilderType> GetObTypeSet()
        {
            var set = BuildInfoMod.Instance.Caches.OBTypeSet;
            set.Clear();
            return set;
        }

        public static HashSet<MyDefinitionId> GetDefIdSet()
        {
            var set = BuildInfoMod.Instance.Caches.DefIdSet;
            set.Clear();
            return set;
        }
    }
}
