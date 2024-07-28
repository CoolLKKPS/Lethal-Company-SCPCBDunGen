using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace SCPCBDunGen
{
    public class SCPCBElevNuclearManager : NetworkBehaviour
    {
        public BoxCollider TeleporterTop;
        public BoxCollider TeleporterBot;

        public InteractTrigger ButtonTopIn;
        public InteractTrigger ButtonTopOut;
        public InteractTrigger ButtonBotIn;
        public InteractTrigger ButtonBotOut;

        public AudioSource ElevatorDingTop;
        public AudioSource ElevatorDingBot;
        public AudioSource ElevatorMoveSFXTop;
        public AudioSource ElevatorMoveSFXBot;
        public AudioSource DoorSFXTop;
        public AudioSource DoorSFXBot;

        public Animator DoorTop;
        public Animator DoorBot;

        private bool IsAtTop = true; // Where the fake elevator is currently stationed, true if top, false if bottom
        private bool Active = false; // Server parameter to reject multiple activation at once

        private const float TeleDistanceMagic = 15.36f; // Magic number that is the distance between the ground of each elevator

        private void DisableAllButtons()
        {
            ButtonTopIn.interactable = false;
            ButtonTopOut.interactable = false;
            ButtonBotIn.interactable = false;
            ButtonBotOut.interactable = false;
        }

        // Begin elevator sequence
        [ServerRpc(RequireOwnership = false)]
        public void ActivateServerRpc()
        {
            if (Active) return;
            Active = true;
            ActivateClientRpc(IsAtTop);
            StartCoroutine(ConversionProcess());
        }

        [ClientRpc]
        public void ElevatorDingClientRpc(bool DingTop)
        {
            AudioSource ElevatorDingSFX = DingTop ? ElevatorDingTop : ElevatorDingBot;
            ElevatorDingSFX.Play();
        }

        [ClientRpc]
        public void ElevatorMoveSFXClientRpc(bool SFXTop)
        {
            AudioSource ElevatorDingSFX = SFXTop ? ElevatorMoveSFXTop : ElevatorMoveSFXBot;
            ElevatorDingSFX.Play();
        }

        // Open elevator door and enable buttons depending on where we're going
        [ClientRpc]
        public void FinishClientRpc(bool OpeningTop)
        {
            ButtonTopOut.interactable = !OpeningTop;
            ButtonTopIn.interactable = true;
            ButtonBotOut.interactable = OpeningTop;
            ButtonBotIn.interactable = true;

            DoorTop.SetBoolString("open", OpeningTop);
            DoorBot.SetBoolString("open", !OpeningTop);
        }

        // Close doors, play door SFX and disable buttons
        [ClientRpc]
        public void ActivateClientRpc(bool DoorClosingTop)
        {
            DisableAllButtons();
            DoorTop.SetBoolString("open", false);
            DoorBot.SetBoolString("open", false);

            AudioSource DoorSFX = DoorClosingTop ? DoorSFXTop : DoorSFXBot;
            DoorSFX.Play();
        }

        // ** Item teleportation
        [ClientRpc]
        private void TeleportItemClientRpc(NetworkBehaviourReference netBehaviourRefItem, Vector3 vPosition)
        {
            NetworkBehaviour netBehaviourItem = null;
            netBehaviourRefItem.TryGet(out netBehaviourItem);
            if (netBehaviourItem == null)
            {
                SCPCBDunGen.Logger.LogError("Failed to get grabbable.");
                return;
            }
            GrabbableObject grabbable = (GrabbableObject)netBehaviourItem;
            grabbable.targetFloorPosition = vPosition;
        }

        // ** Player teleportation
        [ClientRpc]
        private void TeleportPlayerClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, Vector3 vPosition)
        {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null)
            {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            playerController.TeleportPlayer(vPosition);
        }

        [ClientRpc]
        private void TeleportEnemyClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, Vector3 vPosition)
        {
            NetworkBehaviour netBehaviourEnemy = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourEnemy);
            if (netBehaviourEnemy == null)
            {
                SCPCBDunGen.Logger.LogError("Failed to get enemy AI.");
                return;
            }
            EnemyAI enemyAI = (EnemyAI)netBehaviourEnemy;
            enemyAI.serverPosition = vPosition;
        }

        IEnumerator ConversionProcess()
        {
            yield return new WaitForSeconds(1.0f); // Wait a second for doors to close before playing elevator noise

            ElevatorMoveSFXClientRpc(IsAtTop);

            yield return new WaitForSeconds(10); // Initial wait before attempting teleport to allow doors to close and elevator moving SFX to finish

            BoxCollider Teleporter = IsAtTop ? TeleporterTop : TeleporterBot;

            SCPCBDunGen.Logger.LogInfo("** BEGINNING ELEVATOR TELEPORT SEQUENCE **");

            // Safety barrier to ensure we don't teleport the same things twice
            List<PlayerControllerB> TeleportedPlayers = new List<PlayerControllerB>();
            List<GrabbableObject> TeleportedGrabbables = new List<GrabbableObject>();
            List<EnemyAI> TeleportedEnemies = new List<EnemyAI>();

            Collider[] containedColliders = Physics.OverlapBox(Teleporter.transform.position + Teleporter.center, Teleporter.size / 2);

            foreach (Collider collider in containedColliders)
            {
                GameObject gameObject = collider.gameObject;

                GrabbableObject grabbable = gameObject.GetComponent<GrabbableObject>();
                // If grabbable item, move it
                if (grabbable != null)
                {
                    if (TeleportedGrabbables.Contains(grabbable))
                    {
                        SCPCBDunGen.Logger.LogWarning("Tried to teleport item twice! Skipping.");
                        continue;
                    }
                    TeleportedGrabbables.Add(grabbable);

                    SCPCBDunGen.Logger.LogInfo($"Teleporting grabbable {gameObject.name}");
                    Vector3 ItemPosition = grabbable.targetFloorPosition;
                    ItemPosition.y += IsAtTop ? -TeleDistanceMagic : TeleDistanceMagic;
                    NetworkBehaviourReference netBehaviourItem = grabbable;
                    TeleportItemClientRpc(netBehaviourItem, ItemPosition);
                    continue;
                }
                // If player, run the player teleport function
                PlayerControllerB playerController = gameObject.GetComponent<PlayerControllerB>();
                if (playerController != null)
                {
                    if (TeleportedPlayers.Contains(playerController))
                    {
                        SCPCBDunGen.Logger.LogWarning("Tried to teleport player twice! Skipping.");
                        continue;
                    }
                    TeleportedPlayers.Add(playerController);

                    Vector3 PlayerPosition = playerController.transform.position;
                    PlayerPosition.y += IsAtTop ? -TeleDistanceMagic : TeleDistanceMagic;
                    NetworkBehaviourReference netBehaviourPlayer = playerController;
                    SCPCBDunGen.Logger.LogInfo($"Teleporting player to {PlayerPosition}");
                    TeleportPlayerClientRpc(netBehaviourPlayer, PlayerPosition);
                    continue;
                }
                // If enemy, teleport them down (enemy AI script is above the collider in hierarchy)
                EnemyAI Enemy = gameObject.GetComponentInParent<EnemyAI>();
                if (Enemy != null)
                {
                    if (TeleportedEnemies.Contains(Enemy))
                    {
                        SCPCBDunGen.Logger.LogWarning("Tried to teleport enemy twice! Skipping.");
                        continue;
                    }
                    TeleportedEnemies.Add(Enemy);

                    Vector3 Target = Enemy.serverPosition;
                    Target.y += IsAtTop ? -TeleDistanceMagic : TeleDistanceMagic;
                    Vector3 NavPosition = RoundManager.Instance.GetNavMeshPosition(Target);
                    SCPCBDunGen.Logger.LogInfo($"Teleporting enemy {gameObject.name} to {NavPosition}");
                    NetworkBehaviourReference netBehaviourEnemy = Enemy;
                    TeleportEnemyClientRpc(netBehaviourEnemy, NavPosition);

                    Enemy.agent.Warp(NavPosition);
                    Enemy.SyncPositionToClients();
                    continue;
                }
                SCPCBDunGen.Logger.LogInfo($"Object with no elevator handling: {gameObject.name}, skipping.");
            }

            // Do the elevator ding after teleporting, then open after a second

            ElevatorDingClientRpc(!IsAtTop);

            yield return new WaitForSeconds(1.0f);

            FinishClientRpc(!IsAtTop);

            IsAtTop = !IsAtTop;
            Active = false;
        }
    }
}
