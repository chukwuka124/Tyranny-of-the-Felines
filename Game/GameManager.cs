using System;
using System.Collections;
using System.Collections.Generic;
using Matryoshka.Abilities;
using Matryoshka.Entity;
using Matryoshka.Lobby;
using Matryoshka.UI;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Video;

namespace Matryoshka.Game
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Singleton;
        
        [SerializeField]
        private GameObject bigPrefab;
        [SerializeField] 
        private GameObject wazoPrefab;
        [SerializeField]
        private GameObject salumonPrefab;
        [SerializeField]
        public GameObject furballPrefab;

        [SerializeField]
        public GameObject healthBar;// { get; private set; }

        [SerializeField] private GameObject mittensPrefab;

        private readonly Vector3 bigStartingPosition = new Vector3(0, 1);
        private readonly Vector3 wazoStartingPosition = new Vector3(-1, -1);
        private readonly Vector3 salumonStartingPosition = new Vector3(1, -1);
        private readonly Vector3 mittensStartingPosition = new Vector3(0, 12);

        private NetworkVariable<bool> isGameOver = new NetworkVariable<bool>(false);
        private NetworkVariable<bool> isVictory = new NetworkVariable<bool>(false);
        private bool pendingGameEnd = false;

        [HideInInspector]
        public List<GameObject> playerObjects;
        [HideInInspector]
        public GameObject mittensObject;

        private GameObject localPlayerObject;
        public bool shouldDespawnFurballs = false;
        public bool won;
        public bool serverShuttingDown = false;
        public bool postGame = false;
        public bool playingOutroVideo = false;
        public bool playedOutroVideo = false;
        public GameObject outroVideoPrefab;
        public const float OutroVideoLength = 62f;
        private const float Escape = 1f;
        private GameObject outroVideoPlayer;

        void Start()
        {
            Singleton = this;

            playerObjects = new List<GameObject>();
            if (NetworkManager.Singleton.IsServer)
            {
                foreach(var player in LobbyManager.Singleton.GetPlayerList())
                {
                    GameObject playerPrefab;
                    Vector3 position;
                    switch (player.GetPlayerClass())
                    {
                        case PlayerClass.Mouse:
                            playerPrefab = bigPrefab;
                            position = bigStartingPosition;
                            break;
                        case PlayerClass.Bird:
                            playerPrefab = wazoPrefab;
                            position = wazoStartingPosition;
                            break;
                        case PlayerClass.Fish:
                            playerPrefab = salumonPrefab;
                            position = salumonStartingPosition;
                            break;
                        default:
                            playerPrefab = null;
                            position = Vector2.zero;
                            break;
                    }
                    
                    GameObject playerObject = Instantiate(playerPrefab, position, Quaternion.identity);
                    position.y += 1;
                    GameObject healthBarObject = Instantiate(healthBar, position, Quaternion.identity);
                    playerObject.GetComponent<Entity.Entity>().SetHealthBar(healthBarObject);
                    playerObject.GetComponent<NetworkObject>().SpawnAsPlayerObject(player.GetId(), true);
                    healthBarObject.GetComponent<NetworkObject>().Spawn(true);
                    playerObjects.Add(playerObject);
                }
                
                GameObject mittensGameObject = Instantiate(mittensPrefab, mittensStartingPosition, Quaternion.identity);
                mittensGameObject.GetComponent<NetworkObject>().Spawn(true);
                mittensObject = mittensGameObject;
            }
            foreach (var player in GameObject.FindGameObjectsWithTag("Player"))
            {
                if (player.GetComponent<Entity.Controller.PlayerController>().IsOwner)
                {
                    localPlayerObject = player;
                    break;
                }
            }
            if (localPlayerObject == null)
            {
                Debug.LogWarning("We are an unknown player.");
            }
        }

        private void FixedUpdate()
        {
            if (!postGame)
            {
                if (IsServer)
                {
                    if (!isGameOver.Value && !pendingGameEnd)
                    {
                        if (mittensObject.GetComponent<Entity.Entity>().GetMittensPhase() == 4)
                        {
                            pendingGameEnd = true;
                            StartCoroutine(Victory());
                        }
                        else
                        {
                            int deadCount = 0;
                            foreach (var player in playerObjects)
                            {
                                if (player.GetComponent<Entity.Entity>().networkPlayerState.Value == PlayerState.Dead)
                                {
                                    deadCount += 1;
                                }
                            }

                            if (deadCount == playerObjects.Count)
                            {
                                pendingGameEnd = true;
                                StartCoroutine(Defeat());
                            }
                        }
                    }
                }

                if (isGameOver.Value)
                {
                    if (PauseMenuUI.Singleton.IsShowing())
                    {
                        PauseMenuUI.Singleton.HidePauseMenuUI();
                    }

                    if (!isVictory.Value)
                    {
                        NetworkManager.Singleton.Shutdown();
                        VictoryDefeatUI.Singleton.ShowVictoryDefeatUI("DEFEAT");
                    }
                    else
                    {
                        won = true;
                    }
                    
                    if (IsServer)
                    {
                        if (LobbyManager.Singleton != null)
                        {
                            LobbyManager.Singleton.gameObject.GetComponent<NetworkObject>().Despawn();
                        }
                        postGame = true;
                        serverShuttingDown = true;
                        StartCoroutine(DelayedShutdown());
                    }
                    else
                    {
                        postGame = true;
                    }
                    
                }
            }
            else
            {
                if (won && !serverShuttingDown)
                {
                    if (!playedOutroVideo)
                    {
                        playedOutroVideo = true;
                        StartOutroVideo();
                    }

                    if (playingOutroVideo && Math.Abs(Input.GetAxis("Cancel") - Escape) < 0.0001)
                    {
                        SkipOutroVideo();
                    }
                }
            }
        }

        private void StartOutroVideo()
        {
            playingOutroVideo = true;
            outroVideoPlayer = Instantiate(outroVideoPrefab);
            HUDUI.Singleton.HideHud();
            CutsceneOverlayUI.Singleton.ShowCutsceneOverlayUI();
            GameObject mainCameraObject = GameObject.Find("Main Camera");
            Camera mainCamera = mainCameraObject.GetComponent<Camera>();
            mainCamera.enabled = true;
            VideoPlayer videoPlayer = outroVideoPlayer.GetComponent<VideoPlayer>();
            videoPlayer.targetCamera = mainCamera;
            videoPlayer.Play();
            StartCoroutine(OutroVideoCountdown());
            NetworkManager.Singleton.gameObject.GetComponent<LobbyStart>().Stop();
            NetworkManager.Singleton.Shutdown();
            NetworkManager.Singleton.gameObject.GetComponent<LobbyStart>().Start();
        }

        private void SkipOutroVideo()
        {
            StopAllCoroutines();
            StopOutroVideo();
        }

        private void StopOutroVideo()
        {
            playingOutroVideo = false;
            CutsceneOverlayUI.Singleton.HideCutsceneOverlayUI();
            Destroy(outroVideoPlayer);
            VictoryDefeatUI.Singleton.ShowVictoryDefeatUI("VICTORY");
        }

        private IEnumerator DelayedShutdown()
        {
            yield return new WaitForSeconds(0.5f);
            serverShuttingDown = false;
        }

        private IEnumerator Defeat()
        {
            yield return new WaitForSeconds(5.0f);
            isGameOver.Value = true;
            isVictory.Value = false;
        }

        private IEnumerator Victory()
        {
            yield return new WaitForSeconds(5.0f);
            isGameOver.Value = true;
            isVictory.Value = true;
        }

        private IEnumerator OutroVideoCountdown()
        {
            yield return new WaitForSeconds(OutroVideoLength);
            StopOutroVideo();
        }

        public static GameObject GetRandomPlayer()
        {
            var choice = UnityEngine.Random.Range(0, Singleton.playerObjects.Count - 1);
            return Singleton.playerObjects[choice];
        }

        public static GameObject GetMittens()
        {
            return Singleton.mittensObject;
        }

        public static GameObject GetEntityWithType(EntityType entityType)
        {
            if (entityType == EntityType.Mittens)
            {
                return GetMittens();
            }

            foreach(var player in Singleton.playerObjects)
            {
                Entity.Entity entity = player.GetComponent<Entity.Entity>();
                if (entity.entityType == entityType)
                {
                    return player;
                }
            }

            return null;
        }

        [ClientRpc]
        public void StartCooldownClientRpc(EntityType entityType, AbilityType ability, float cooldownTime)
        {
            /*if (localPlayerObject.GetComponent<Entity.Entity>().entityType == entityType)
            {
                HUDUI.Singleton.StartCooldownTimer(entityType, ability, cooldownTime);
            }*/
            HUDUI.Singleton.StartCooldownTimer(entityType, ability, cooldownTime);

        }
    }
}
