using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BIS
{
    /// <summary>
    /// A walking mech drawn in place of a hover tank's hull (Quaternius's CC0 Animated Mech Pack, Resources/Models/Mechs).
    /// The vehicle itself is unchanged - same health, weapons, collider and controls; this only draws it. Idle, walk and
    /// run clips are blended by how fast the mech is really moving over the ground (measured here from its position, so a
    /// Wi-Fi copy animates the same as the one being driven), and played through a PlayableGraph, so no controller asset
    /// is needed.
    /// </summary>
    public class WalkerRig : MonoBehaviour
    {
        PlayableGraph graph;
        AnimationMixerPlayable mix;
        AnimationClipPlayable idle, walk, run;
        Vector3 lastPos;
        float speed;
        public float stride = 4.2f;    // metres covered by one walk cycle at this scale
        public float runFrom = 9f;     // ground speed (m/s) where running takes over

        /// <summary>Puts the mech model under <paramref name="parent"/> standing <paramref name="drop"/> metres below it.</summary>
        public static WalkerRig Make(string path, Transform parent, float drop, float scale, Material mat)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (!prefab) return null;
            var go = Instantiate(prefab, parent, false);
            go.name = "walker";
            go.transform.localPosition = new Vector3(0, -drop, 0);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);   // the pack's mechs face -z
            go.transform.localScale = Vector3.one * scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Vehicle;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mat) r.sharedMaterial = mat;
                r.updateWhenOffscreen = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var anim = go.GetComponent<Animator>();
            if (!anim) anim = go.AddComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullCompletely;
            var rig = go.AddComponent<WalkerRig>();
            rig.Setup(anim, path);
            return rig;
        }

        static AnimationClip Clip(AnimationClip[] all, string name)
        {
            foreach (var c in all) if (c.name == name) return c;
            foreach (var c in all) if (c.name.EndsWith("|" + name)) return c;
            return null;
        }

        void Setup(Animator anim, string path)
        {
            var clips = Resources.LoadAll<AnimationClip>(path);
            var ci = Clip(clips, "Idle"); var cw = Clip(clips, "Walk"); var cr = Clip(clips, "Run");
            if (!ci || !cw) { enabled = false; return; }
            if (!cr) cr = cw;
            graph = PlayableGraph.Create("walker");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mix = AnimationMixerPlayable.Create(graph, 3);
            idle = AnimationClipPlayable.Create(graph, ci);
            walk = AnimationClipPlayable.Create(graph, cw);
            run = AnimationClipPlayable.Create(graph, cr);
            graph.Connect(idle, 0, mix, 0); graph.Connect(walk, 0, mix, 1); graph.Connect(run, 0, mix, 2);
            mix.SetInputWeight(0, 1);
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            output.SetSourcePlayable(mix);
            graph.Play();
            lastPos = transform.position;
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector3 d = transform.position - lastPos; d.y = 0;
            lastPos = transform.position;
            speed = Mathf.Lerp(speed, Mathf.Min(40, d.magnitude / dt), 1 - Mathf.Exp(-8 * dt));
            // weights: standing, walking, running
            float w = Mathf.InverseLerp(0.4f, 2.5f, speed), r = Mathf.InverseLerp(runFrom - 2, runFrom + 2, speed);
            mix.SetInputWeight(0, 1 - w);
            mix.SetInputWeight(1, w * (1 - r));
            mix.SetInputWeight(2, w * r);
            // legs keep pace with the ground: one cycle per stride (a run stride is longer)
            float cyc = Mathf.Max(0.25f, speed / stride);
            walk.SetSpeed(cyc * walk.GetAnimationClip().length);
            run.SetSpeed(Mathf.Max(0.5f, speed / (stride * 1.7f)) * run.GetAnimationClip().length);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
