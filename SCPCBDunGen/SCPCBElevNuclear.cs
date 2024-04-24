using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace SCPCBDunGen
{
    public class SCPCBElevNuclearTeleporter : NetworkBehaviour
    {
        SCPCBElevNuclearTeleporter()
        {
            lContainedObjects = [];
        }

        // Store list of things inside
        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkManager.IsServer) return;
            SCPCBDunGen.Logger.LogInfo($"New thing entered elevator trigger: {other.gameObject.name}.");
            lContainedObjects.Add(other.gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!NetworkManager.IsServer) return;
            SCPCBDunGen.Logger.LogInfo($"Thing has left elevator trigger: {other.gameObject.name}.");
            lContainedObjects.Remove(other.gameObject);
        }

        public List<GameObject> lContainedObjects;
    }

    public class SCPCBElevNuclearManager : NetworkBehaviour
    {
        public SCPCBElevNuclearTeleporter TeleporterTop;
        public SCPCBElevNuclearTeleporter TeleporterBot;

        public InteractTrigger ButtonTopIn;
        public InteractTrigger ButtonTopOut;
        public InteractTrigger ButtonBotIn;
        public InteractTrigger ButtonBotOut;

        public AudioSource ElevatorDingTop;
        public AudioSource ElevatorDingBot;

        public Animator DoorTop;
        public Animator DoorBot;

        private bool IsAtTop = true; // Where the fake elevator is currently stationed, true if top, false if bottom
        private bool Active = false; // Server parameter to reject multiple activation at once

        private const double TeleDistanceMagic = 15.09; // Magic number that is the distance between the ground of each elevator

        private void DisableAllButtons()
        {
            ButtonTopIn.enabled = false;
            ButtonTopOut.enabled = false;
            ButtonBotIn.enabled = false;
            ButtonBotOut.enabled = false;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ActivateServerRpc()
        {
            if (Active) return;
            Active = true;
            ActivateClientRpc();
            StartCoroutine(ConversionProcess());
        }

        [ClientRpc]
        public void ActivateClientRpc()
        {
            DisableAllButtons();
            DoorTop.SetBoolString("open", false);
            DoorBot.SetBoolString("open", false);
        }

        // ** Teleport players up/down
        [ClientRpc]
        private void TeleportPlayerClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, bool bTeleDown)
        {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null)
            {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB PlayerController = (PlayerControllerB)netBehaviourPlayer;
            Vector3 PlayerPosition = PlayerController.serverPlayerPosition;
            PlayerPosition += bTeleDown ?  : ;
            PlayerController.TeleportPlayer(vPosition);
        }

        IEnumerator ConversionProcess()
        {
            yield return new WaitForSeconds(5); // Initial wait before attempting teleport to allow doors to close

            RoundManager.Instance;

            List<NetworkObjectReference> lNetworkObjectReferences = new List<NetworkObjectReference>();
            List<int> lScrapValues = new List<int>();
            bool bChargeBatteries = (iCurrentState > 1);

            Dictionary<Item, List<Item>> dcCurrentMapping = GetItemMapping();
            SCPCBDunGen.Logger.LogInfo($"Contained item count: {InputStore.lContainedObjects.Count}");
            foreach (GameObject gameObject in InputStore.lContainedObjects)
            {
                GrabbableObject grabbable = gameObject.GetComponent<GrabbableObject>();
                // If grabbable item, convert it
                if (grabbable != null)
                {
                    ConvertItem(lNetworkObjectReferences, lScrapValues, dcCurrentMapping, grabbable);
                    continue;
                }
                // Special case for players
                PlayerControllerB playerController = gameObject.GetComponent<PlayerControllerB>();
                if (playerController != null)
                {
                    ConvertPlayer(playerController);
                    continue;
                }
                // TODO enemy conversions
            }
            SCPCBDunGen.Logger.LogInfo("Finished spawning scrap, syncing with clients");
            SpawnItemsClientRpc(lNetworkObjectReferences.ToArray(), lScrapValues.ToArray(), bChargeBatteries);
            InputStore.lContainedObjects.Clear(); // Empty list for next runthrough
            yield return new WaitForSeconds(7); // 14 seconds (7 * 2) is the duration of the refining SFX (at the part where the bell dings is when we open the doors)
            RefineFinishClientRpc();
            bActive = false;
        }
    }
}
