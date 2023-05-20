using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx;

namespace DD2
{
    [BepInPlugin("binarizer.plugin.dd2.function_sets", "功能合集 by Binarizer", "1.0")]
    public class PluginBinarizer : BaseUnityPlugin
    {
        void RegisterHook(IHook hook)
        {
            hook.OnRegister(this);
            hook.GetRegisterTypes().Do(t => { Harmony.CreateAndPatchAll(t); Console.WriteLine("Patch " + t.Name); });
            hooks.Add(hook);
        }

        private List<IHook> hooks = new List<IHook>();

        void Awake()
        {
            Console.WriteLine("美好的初始化开始");
            RegisterHook(new HookGenerals());
        }

        void Start()
        {
            Console.WriteLine("美好的第一帧开始");
        }

        void Update()
        {
            Console.WriteLine("美好的帧循环开始");
            foreach (IHook hook in hooks)
            {
                hook.OnUpdate();
            }
        }
    }
}
