using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BackInTheBox;

[BepInPlugin("fururikawa.BackInTheBox", "fururikawa.BackInTheBox", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<KeyCode> _actionKey;
    private ConfigEntry<KeyCode> _actionModifier;
    private static int NEXUS_ID = 185;
    public Plugin()
    {
        _actionKey = Config.Bind<KeyCode>("Controls", "Action Key", KeyCode.E, "The Key you want to use to box your animals!");
        _actionModifier = Config.Bind<KeyCode>("Controls", "Action Key Modifier", KeyCode.None, "Any modifiers to go with your order?");
        Config.Bind<int>("Other", "Nexus ID", NEXUS_ID, "Do not change me. Seriously, move that cursor away!");
    }
    private void Awake()
    {
        Harmony.CreateAndPatchAll(typeof(AnimalCarryBoxPatch));

        Logger.LogInfo($"Plugin fururikawa.BackInTheBox is loaded!");
    }

    private void FixedUpdate()
    {
        if ((_actionModifier.Value == KeyCode.None || Input.GetKey(_actionModifier.Value)) && Input.GetKeyDown(_actionKey.Value))
        {
            var myChar = NetworkMapSharer.share.localChar;
            if (myChar && MenuButtonsTop.menu.closed && Inventory.inv.canMoveChar())
            {
                RaycastHit raycastHit;
                Vector3 p1 = myChar.transform.position;
                Vector3 p2 = p1 + Vector3.up * myChar.col.height;
                if (Physics.CapsuleCast(p1, p2, myChar.col.radius / 2f, myChar.transform.forward, out raycastHit, 3.1f, LayerMask.GetMask("Prey")))
                {
                    InteractableObject[] objs = raycastHit.collider.GetComponents<InteractableObject>();

                    foreach (var obj in objs)
                    {
                        if (obj.isFarmAnimal != null)
                        {
                            var animal = obj.isFarmAnimal;

                            GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(FarmAnimalMenu.menu.animalBoxPrefab, animal.transform.position, animal.transform.rotation);
                            var carryBox = gameObject.GetComponent<AnimalCarryBox>();
                            carryBox.setUp(animal.getDetails().animalId, animal.getDetails().animalVariation, animal.getAnimalName());

                            var animalAi = raycastHit.collider.GetComponent<AnimalAI>();
                            animalAi.myAgent.enabled = false;
                            animalAi.enabled = false;

                            SoundManager.manage.playASoundAtPoint(animal.animalPatSound, carryBox.transform.position, 1.4f, 0.8f);

                            animal.gameObject.SetActive(false);
                            SoundManager.manage.playASoundAtPoint(SoundManager.manage.plantSeed, carryBox.transform.position, 1.4f, 0.8f);
                            NetworkServer.Spawn(gameObject, (NetworkConnection)null);

                            for (int i = 0; i < carryBox.boxSides.Length; i++)
                            {
                                ParticleManager.manage.emitParticleAtPosition(ParticleManager.manage.allParts[3], carryBox.boxSides[i].position, 25);
                            }

                            var carry = gameObject.GetComponent<PickUpAndCarry>();
                            carry.dropToPos = animal.transform.position.y;
                            carry.transform.position = animal.transform.position;
                        }
                    }
                }
            }
        }
    }
}

[HarmonyPatch]
public static class AnimalCarryBoxPatch
{
    [HarmonyPatch(typeof(AnimalCarryBox), nameof(AnimalCarryBox.releaseAnimal))]
    [HarmonyPrefix]
    private static bool releaseAnimal(AnimalCarryBox __instance)
    {
        var existing = FarmAnimalManager.manage.farmAnimalDetails
            .FirstOrDefault(x => x.animalId == __instance.animalId && x.animalName == __instance.animalName && x.animalVariation == __instance.variation);

        if (existing == null)
            return true;

        existing.setPosition(__instance.transform.position);

        FarmAnimal animal = FarmAnimalManager.manage.activeAnimalAgents[existing.agentListId];
        var animalAi = animal.GetComponent<AnimalAI>();

        animalAi.transform.position = __instance.transform.position;
        animalAi.myAgent.transform.position = __instance.transform.position;
        animal.transform.position = __instance.transform.position;

        animal.gameObject.SetActive(true);
        animalAi.myAgent.enabled = true;
        animalAi.enabled = true;
        animalAi.forceSetUp();

        return false;
    }

    [HarmonyPatch(typeof(CharPickUp), "Update")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        var label1 = il.DefineLabel();
        var label2 = il.DefineLabel();
        var label3 = il.DefineLabel();

        var _instructions = new CodeMatcher(instructions);

        _instructions
        .MatchForward(true,
            new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(NotificationManager), "manage")),
            new CodeMatch(OpCodes.Ldc_I4_0),
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(NotificationManager), "hintWindowOpen", new Type[] { typeof(NotificationManager.toolTipType) })))
        .MatchForward(false,
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(CharInteract), "canTileBePickedUp")))
        .Advance(1);

        Label labelToClear = (Label)_instructions.Operand;

        _instructions
        .SetInstruction(new CodeInstruction(OpCodes.Brfalse, label1))
        .MatchForward(false,
            new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(NotificationManager), "manage")),
            new CodeMatch(OpCodes.Ldc_I4_0),
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(NotificationManager), "hintWindowOpen", new Type[] { typeof(NotificationManager.toolTipType) })))

        .Insert(new CodeInstruction(OpCodes.Ldarg_0)).Labels.Add(label1);

        _instructions.Advance(1)
        .InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(CharPickUp), "myChar")))
        .InsertAndAdvance(Transpilers.EmitDelegate<Func<CharMovement, bool>>(myChar =>
        {
            if (myChar)
            {
                RaycastHit raycastHit;
                Vector3 p1 = myChar.transform.position;
                Vector3 p2 = p1 + Vector3.up * myChar.col.height;
                if (Physics.CapsuleCast(p1, p2, myChar.col.radius / 2f, myChar.transform.forward, out raycastHit, 3.1f, LayerMask.GetMask("Prey")))
                {
                    InteractableObject[] objs = raycastHit.collider.GetComponents<InteractableObject>();

                    foreach (var obj in objs)
                    {
                        if (obj.isFarmAnimal != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }))
        .InsertAndAdvance(new CodeInstruction(OpCodes.Brfalse_S, label2))
        .InsertAndAdvance(new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(NotificationManager), "manage")))
        .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_3))
        .InsertAndAdvance(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(NotificationManager), "hintWindowOpen", new Type[] { typeof(NotificationManager.toolTipType) })))
        .InsertAndAdvance(new CodeInstruction(OpCodes.Br_S, label3));

        var labelToSwitch = _instructions.Labels.Single();
        _instructions.Labels.Remove(labelToSwitch);
        _instructions.Labels.Add(label2);

        _instructions.Advance(3)
            .AddLabels(new Label[] { labelToSwitch, label3 });

        _instructions.Labels.Remove(labelToClear);

        return _instructions.InstructionEnumeration();
    }
}