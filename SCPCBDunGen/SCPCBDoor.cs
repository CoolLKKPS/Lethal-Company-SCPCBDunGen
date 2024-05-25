using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine;
using UnityEngine.Yoga;
using DunGen;
using System.Linq;
using System.Collections;

namespace SCPCBDunGen
{
    public class SCPDoorMover : NetworkBehaviour
    {
        public NavMeshObstacle navObstacle;
        public Animator doors;
        public List<AudioClip> doorAudioClips;
        public AudioClip doorAudioClipFast;
        public AudioSource doorSFXSource;

        public InteractTrigger ButtonA;
        public InteractTrigger ButtonB;

        bool bDoorOpen = false;
        bool bDoorWaiting = false; // In the middle of opening or closing, server only parameter

        private List<EnemyAICollisionDetect> EnemiesInCollider = new List<EnemyAICollisionDetect>();

        private void OnTriggerEnter(Collider other) {
            if (NetworkManager.Singleton == null || !IsServer || !other.CompareTag("Enemy")) return;
            EnemyAICollisionDetect collisionDetect = other.GetComponent<EnemyAICollisionDetect>();
            if (collisionDetect == null) return;

            SCPCBDunGen.Logger.LogInfo($"Enemy entered trigger: {collisionDetect.mainScript.enemyType.name}");
            EnemiesInCollider.Add(collisionDetect);
        }

        private void OnTriggerExit(Collider other) {
            if (NetworkManager.Singleton == null || !IsServer || !other.CompareTag("Enemy")) return;
            EnemyAICollisionDetect collisionDetect = other.GetComponent<EnemyAICollisionDetect>();
            if (collisionDetect == null) return;

            if (!EnemiesInCollider.Remove(collisionDetect)) {
                SCPCBDunGen.Logger.LogWarning("Enemy left door trigger but somehow wasn't detected in trigger entry.");
            }
        }

        private void Update() {
            // Server only update
            if (NetworkManager.Singleton == null || !IsServer) return;

            if (bDoorOpen) return; // Door is already open, enemies never close doors so exit early
            if (bDoorWaiting) return; // Already in the middle of something
            if (EnemiesInCollider.Count == 0) return; // No enemies, nothing to open the door

            SCPCBDunGen.Logger.LogInfo("Enemy attempting to open door...");

            float fHighestMult = 0.0f;

            foreach (EnemyAICollisionDetect enemy in EnemiesInCollider) {
                EnemyAI enemyAI = enemy.mainScript;
                if (enemyAI.isEnemyDead) continue; // Skip dead enemies

                SCPCBDunGen.Logger.LogInfo($"Enemy {enemyAI.enemyType.name} with open mult {enemyAI.openDoorSpeedMultiplier}");

                float fEnemyDoorOpenSpeed = enemyAI.openDoorSpeedMultiplier;
                if (enemyAI.enemyType.name == "MaskedPlayerEnemy") fEnemyDoorOpenSpeed = 1.0f; // Force masked enemies to open doors at player speeds despite their 2.0 door opening speed
                if (enemyAI.enemyType.name == "Crawler") fEnemyDoorOpenSpeed = 2.0f;           // Inversely, make thumpers open doors extremely quickly despite their 0.3 door opening speed

                fHighestMult = Math.Max(fHighestMult, fEnemyDoorOpenSpeed);
            }

            SCPCBDunGen.Logger.LogInfo($"Highest multiplier is {fHighestMult}.");

            if (fHighestMult != 0.0f) {
                // Something is at the door that wants to open it
                SCPCBDunGen.Logger.LogInfo("Door being opened.");
                if (fHighestMult > 1.5f) {
                    // This enemy wants the door open fast, use the faster animation
                    OpenDoorFastServerRpc();
                } else {
                    ToggleDoorServerRpc();
                }
            }
        }

        [ServerRpc]
        public void OpenDoorFastServerRpc() {
            SCPCBDunGen.Logger.LogInfo("Opening door fast [SERVER].");
            bDoorWaiting = true;
            bDoorOpen = true;
            navObstacle.enabled = false;
            OpenDoorFastClientRpc();
            StartCoroutine(DoorToggleButtonUsable());
        }

        [ClientRpc]
        public void OpenDoorFastClientRpc() {
            SCPCBDunGen.Logger.LogInfo("Opening door fast [CLIENT].");
            bDoorWaiting = true;
            bDoorOpen = true;
            ButtonA.interactable = false;
            ButtonB.interactable = false;
            navObstacle.enabled = false;
            doorSFXSource.PlayOneShot(doorAudioClipFast);
            doors.SetTrigger("openfast");
        }

        IEnumerator DoorToggleButtonUsable() {
            // If the door was opened normally, this lines up with the animation + 1 second of buffer that can be interrupted by enemies
            // Also doubles as an extended buffer if the door was opened quickly so the player can't cheese enemies by constantly closing the door
            yield return new WaitForSeconds(1.0f);
            bDoorWaiting = false;
            yield return new WaitForSeconds(1.0f);
            EnableDoorButtonClientRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ToggleDoorServerRpc() {
            if (bDoorWaiting) return;
            // If true the door is opening, otherwise it's closing
            bool bDoorOpening = !bDoorOpen;
            string sNewStateLog = bDoorOpening ? "opening" : "closing";
            SCPCBDunGen.Logger.LogInfo($"Door is {sNewStateLog}.");
            bDoorWaiting = true;
            bDoorOpen = bDoorOpening;
            navObstacle.enabled = !bDoorOpening; // Nav obstacle state should be opposite of what the door state is (opening == disabled, closing == enabled)
            ToggleDoorClientRpc(bDoorOpen);
            StartCoroutine(DoorToggleButtonUsable());
        }

        [ClientRpc]
        public void ToggleDoorClientRpc(bool _bDoorOpen) {
            bDoorOpen = _bDoorOpen;
            ButtonA.interactable = false;
            ButtonB.interactable = false;
            navObstacle.enabled = !_bDoorOpen;
            doorSFXSource.PlayOneShot(doorAudioClips[UnityEngine.Random.Range(0, doorAudioClips.Count)]);
            doors.SetTrigger(bDoorOpen ? "open" : "close");
        }

        [ClientRpc]
        public void EnableDoorButtonClientRpc() {
            ButtonA.interactable = true;
            ButtonB.interactable = true;
        }
    }
}
