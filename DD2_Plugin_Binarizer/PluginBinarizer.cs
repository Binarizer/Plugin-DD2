using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx;

namespace DD2
{
    [BepInPlugin("binarizer.plugin.dd2", "功能合集 by Binarizer", "1.4.2")]
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
            Console.WriteLine("Main Awake: Initialize Hooks");
            RegisterHook(new HookGenerals());
        }

        void Start()
        {
            Console.WriteLine("Main Start: ");
        }

        void Update()
        {
            Console.WriteLine("Main Update: ");
            foreach (IHook hook in hooks)
            {
                hook.OnUpdate();
            }
        }
    }
}
