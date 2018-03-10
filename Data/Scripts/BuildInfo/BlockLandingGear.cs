﻿using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.BuildInfo
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LandingGear), useEntityUpdate: false)]
    public class BlockLandingGear : BlockDataBase
    {
        public List<MyOrientedBoundingBoxD> magents = new List<MyOrientedBoundingBoxD>();

        public override bool IsValid(IMyCubeBlock block, MyCubeBlockDefinition def)
        {
            bool success = false;
            var dummies = BuildInfo.instance.dummies;
            dummies.Clear();
            block.Model.GetDummies(dummies);

            // HACK copied from MyLandingGear.LoadDummies()
            var lockPositions = (from s in dummies
                                 where s.Key.ToLower().Contains("gear_lock")
                                 select s.Value.Matrix).ToArray<Matrix>();

            dummies.Clear();

            if(lockPositions.Length == 0)
                return false;

            var wm = block.WorldMatrix;

            for(int i = 0; i < lockPositions.Length; ++i)
            {
                var m = lockPositions[i];

                // HACK copied from MyLandingGear.GetBoxFromMatrix()
                var mn = MatrixD.Normalize(m);
                var orientation = Quaternion.CreateFromRotationMatrix(mn);
                var halfExtents = Vector3.Abs(m.Scale) / 2f;
                halfExtents *= new Vector3(2f, 1f, 2f);
                orientation.Normalize();

                magents.Add(new MyOrientedBoundingBoxD(mn.Translation, halfExtents, orientation));
            }

            return success;
        }
    }
}