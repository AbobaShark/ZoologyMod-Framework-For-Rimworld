using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    internal static class SatisfyBasicNeedsThinkTreeInjector
    {
        private const string InsertTag = "Zoology_SatisfyBasicNeeds";
        private static readonly FieldInfo ThinkNodeSubNodesField = AccessTools.Field(typeof(ThinkNode), "subNodes");
        private static readonly FieldInfo ThinkNodeParentField = AccessTools.Field(typeof(ThinkNode), "parent");

        static SatisfyBasicNeedsThinkTreeInjector()
        {
            Inject();
        }

        private static void Inject()
        {
            try
            {
                ThinkTreeDef def = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("SatisfyBasicNeeds");
                if (def?.thinkRoot == null)
                {
                    return;
                }

                ThinkNode root = def.thinkRoot;
                List<ThinkNode> subNodes = GetSubNodes(root);
                if (subNodes == null)
                {
                    return;
                }

                for (int i = 0; i < subNodes.Count; i++)
                {
                    if (subNodes[i] is ThinkNode_SubtreesByTag existing
                        && string.Equals(existing.insertTag, InsertTag, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                ThinkNode_SubtreesByTag node = new ThinkNode_SubtreesByTag();
                node.insertTag = InsertTag;

                subNodes.Insert(0, node);
                SetParent(node, root);
                node.ResolveSubnodesAndRecur();

                if (Prefs.DevMode)
                {
                    Log.Message($"[Zoology] Inserted SatisfyBasicNeeds hook '{InsertTag}'.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Failed to inject SatisfyBasicNeeds insertTag '{InsertTag}': {ex}");
            }
        }

        private static List<ThinkNode> GetSubNodes(ThinkNode node)
        {
            return ThinkNodeSubNodesField?.GetValue(node) as List<ThinkNode>;
        }

        private static void SetParent(ThinkNode node, ThinkNode parent)
        {
            if (ThinkNodeParentField != null)
            {
                ThinkNodeParentField.SetValue(node, parent);
            }
        }
    }
}
