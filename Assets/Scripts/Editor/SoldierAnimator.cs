using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BIS.EditorTools
{
    /// <summary>
    /// Builds Resources/Models/Characters/Soldier.controller from the Quaternius Universal Animation Library clips:
    /// base layer = Move (Idle / Walk / Jog / Sprint by Speed), Crouch (idle / forward by Speed), Air (jump loop),
    /// Dead (any state, trigger Die); arms layer (masked to the arms) = Rifle (two-handed pistol stance), Sword,
    /// Swing (sword attack, back to Sword), Spell and Cast. SoldierRig drives Speed / Crouch / Grounded / Die and
    /// plays the arms states by name.
    /// </summary>
    public static class SoldierAnimator
    {
        const string Fbx = "Assets/Resources/Models/Characters/UAL_Mannequin.fbx";
        const string Out = "Assets/Resources/Models/Characters/Soldier.controller";
        const string MaskOut = "Assets/Resources/Models/Characters/SoldierArms.mask";

        [MenuItem("Battalions/Build Soldier Animator")]
        public static void Build()
        {
            var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(Fbx))
                if (o is AnimationClip c) clips[c.name.Replace("Rig|", "")] = c;
            AnimationClip C(string n) { if (!clips.TryGetValue(n, out var c)) Debug.LogError("missing clip " + n); return c; }

            AssetDatabase.DeleteAsset(Out);
            var ctl = AnimatorController.CreateAnimatorControllerAtPath(Out);
            ctl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ctl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctl.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            ctl.AddParameter("Knocked", AnimatorControllerParameterType.Bool);   // royale duos: down and crawling
            ctl.AddParameter("Revive", AnimatorControllerParameterType.Bool);    // kneeling over a downed mate
            ctl.AddParameter("CrawlRate", AnimatorControllerParameterType.Float); // how fast the crawl stroke plays (nearly still when not moving)
            foreach (var p in ctl.parameters) if (p.name == "Grounded") p.defaultBool = true;
            var ps = ctl.parameters; foreach (var p in ps) if (p.name == "Grounded") p.defaultBool = true; ctl.parameters = ps;

            var sm = ctl.layers[0].stateMachine;
            BlendTree Tree(string name, params (string clip, float at)[] parts)
            {
                var t = new BlendTree { name = name, blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(t, ctl);
                foreach (var p in parts) t.AddChild(C(p.clip), p.at);
                return t;
            }
            var move = sm.AddState("Move"); move.motion = Tree("MoveTree", ("Idle_Loop", 0), ("Walk_Loop", 1.8f), ("Jog_Fwd_Loop", 4.6f), ("Sprint_Loop", 8f));
            var crouch = sm.AddState("Crouch"); crouch.motion = Tree("CrouchTree", ("Crouch_Idle_Loop", 0), ("Crouch_Fwd_Loop", 2.5f));
            var air = sm.AddState("Air"); air.motion = C("Jump_Loop");
            var dead = sm.AddState("Dead"); dead.motion = C("Death01");
            // the forward swim stroke laid on the ground = a crawl (the idle swim clip treads water upright, so it isn't used);
            // standing still it plays very slowly
            var down = sm.AddState("Down"); down.motion = C("Swim_Fwd_Loop"); down.speedParameterActive = true; down.speedParameter = "CrawlRate";
            var revive = sm.AddState("Revive"); revive.motion = C("Fixing_Kneeling");
            sm.defaultState = move;

            AnimatorStateTransition T(AnimatorState a, AnimatorState b, float dur)
            {
                var t = a.AddTransition(b); t.hasExitTime = false; t.duration = dur; t.exitTime = 0; return t;
            }
            T(move, crouch, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");
            T(crouch, move, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
            T(move, air, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(crouch, air, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(air, move, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            var toDown = sm.AddAnyStateTransition(down); toDown.hasExitTime = false; toDown.duration = 0.2f; toDown.canTransitionToSelf = false;
            toDown.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
            T(down, move, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
            T(move, revive, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Revive");
            T(crouch, revive, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Revive");
            T(revive, move, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Revive");
            var toDead = sm.AddAnyStateTransition(dead); toDead.hasExitTime = false; toDead.duration = 0.1f; toDead.canTransitionToSelf = false;
            toDead.AddCondition(AnimatorConditionMode.If, 0, "Die");

            // arms layer
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            }
            AssetDatabase.DeleteAsset(MaskOut);
            AssetDatabase.CreateAsset(mask, MaskOut);
            ctl.AddLayer("Arms");
            var layers = ctl.layers;
            layers[1].avatarMask = mask; layers[1].defaultWeight = 1; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctl.layers = layers;
            var am = ctl.layers[1].stateMachine;
            var rifle = am.AddState("Rifle"); rifle.motion = C("Pistol_Idle_Loop");
            var sword = am.AddState("Sword"); sword.motion = C("Sword_Idle");
            var swing = am.AddState("Swing"); swing.motion = C("Sword_Attack"); swing.speed = 1.6f;
            var swing2 = am.AddState("Swing2"); swing2.motion = C("Sword_Attack"); swing2.speed = 1.75f; swing2.mirror = true;   // the combo's second cut, the other way
            var spinS = am.AddState("Spin"); spinS.motion = C("Sword_Attack"); spinS.speed = 2.1f;                              // the third: the body turns a full circle in code
            var spell = am.AddState("Spell"); spell.motion = C("Spell_Simple_Idle_Loop");
            var cast = am.AddState("Cast"); cast.motion = C("Spell_Simple_Shoot");
            am.defaultState = rifle;
            var back = swing.AddTransition(sword); back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.1f;
            var back3 = swing2.AddTransition(sword); back3.hasExitTime = true; back3.exitTime = 0.85f; back3.duration = 0.1f;
            var back4 = spinS.AddTransition(sword); back4.hasExitTime = true; back4.exitTime = 0.85f; back4.duration = 0.1f;
            var back2 = cast.AddTransition(spell); back2.hasExitTime = true; back2.exitTime = 0.85f; back2.duration = 0.1f;
            AssetDatabase.SaveAssets();
            Debug.Log("Soldier animator built: " + Out);
        }
    }
}
