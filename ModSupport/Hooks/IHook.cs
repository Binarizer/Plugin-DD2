using BepInEx.Unity.Mono;
using System;
using System.Collections.Generic;

interface IHook
{
    IEnumerable<Type> GetRegisterTypes();

    void OnRegister(BaseUnityPlugin plugin);

    void OnUpdate();
}
