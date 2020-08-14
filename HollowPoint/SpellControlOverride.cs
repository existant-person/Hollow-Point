﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections;
using ModCommon.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine.SceneManagement;
using static UnityEngine.Random ;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using static HollowPoint.HollowPointEnums;

using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Reflection;


namespace HollowPoint
{
    class SpellControlOverride : MonoBehaviour
    {
        public static bool isUsingGun = false;
        //static UnityEngine.Random rand = new UnityEngine.Random();
        PlayMakerFSM nailArtFSM = null;
        PlayMakerFSM glowingWombFSM = null;
        public static bool canSwap = true;
        public float swapTimer = 30f;

        float grenadeCooldown = 30f;
        float airstrikeCooldown = 300f;
        float typhoonTimer = 20f;
        float infuseTimer = 20f;

        PlayMakerFSM spellControl;
        static GameObject sharpFlash;
        public static GameObject focusBurstAnim;

        public static float buff_duration = 0;
        public static bool buffActive = false;
        float buff_constantSoul_timer = 0;

        public static GameObject artifactActivatedEffect;
        static GameObject infusionSoundGO;

        private ILHook removeFocusCost;

        public void Awake()
        {
            StartCoroutine(InitSpellControl());

            //removeFocusCost = new ILHook(typeof(HeroController).GetNestedType("<DisableFocusCost>c__Iterator1F", BindingFlags.NonPublic | BindingFlags.Instance).GetMethod("TakeMP"), RemoveFocusSoulCost);
        }

        private static void RemoveFocusSoulCost(ILContext il)
        {
            //var cursor = new ILCursor(il).Goto(0);
            //cursor.TryGotoNext(out ILCursor[] cursors, x => x.MatchCallvirt(typeof(HeroController), "TakeMP"), x => x.MatchLdcI4(1));

           // for (int i = cursors.Length - 1; i >= 0; i--)
                //cursors[i].Remove();
        }

        void Update()
        {
            if (OrientationHandler.pressingAttack && typhoonTimer > 0)
            {
                typhoonTimer = -1;

                int pelletAmnt = (PlayerData.instance.quakeLevel == 2) ? 8 : 6; 
                StartCoroutine(SpawnGasPulse(HeroController.instance.transform.position, pelletAmnt));
            }
        }

        void FixedUpdate()
        {
            if (swapTimer > 0)
            {
                swapTimer -= Time.deltaTime * 30f;
                canSwap = false;
            }
            else
            {
                canSwap = true;
            }

            if(grenadeCooldown > 0)
            {
                grenadeCooldown -= Time.deltaTime * 30f;
            }

            if(airstrikeCooldown > 0)
            {
                airstrikeCooldown -= Time.deltaTime * 30f;
            }

            if (typhoonTimer > 0)
            {
                typhoonTimer -= Time.deltaTime * 30f;
            }
            //BUFFS TIMER HANDLERS

            if(buff_duration > 0)
            {
                buff_duration -= Time.deltaTime * 10f;
            }
            else
            {
                buffActive = false;
                return;
            }

            if (buff_constantSoul_timer < 0)
            {
                buff_constantSoul_timer = 5f;
                HeroController.instance.AddMPChargeSpa(3);
            }
            else
            {
                buff_constantSoul_timer -= Time.deltaTime * 10f;
            }

        }

        public IEnumerator InitSpellControl()
        {
            while (HeroController.instance == null)
            {
                yield return null;
            }
      
            try
            {
                //Makes hatchlings free
                glowingWombFSM = GameObject.Find("Charm Effects").LocateMyFSM("Hatchling Spawn");
                glowingWombFSM.GetAction<IntCompare>("Can Hatch?", 2).integer2.Value = 0;
                glowingWombFSM.GetAction<Wait>("Equipped", 0).time.Value = 2f;
                glowingWombFSM.RemoveAction("Hatch", 0); //Removes the soul consume on spawn

                infusionSoundGO = new GameObject("infusionSoundGO", typeof(AudioSource));
                DontDestroyOnLoad(infusionSoundGO);

                //Get focus burst and shade dash flash game objects to spawn later
                focusBurstAnim = HeroController.instance.spellControl.FsmVariables.GetFsmGameObject("Focus Burst Anim").Value;
                sharpFlash = HeroController.instance.spellControl.FsmVariables.GetFsmGameObject("SD Sharp Flash").Value;

                //Instantiate(qTrail.Value, HeroController.instance.transform).SetActive(true);

                spellControl = HeroController.instance.spellControl;
                //spellControl.GetAction<GetPlayerDataInt>("Can Focus?", 1).storeValue = 0;
                nailArtFSM = HeroController.instance.gameObject.LocateMyFSM("Nail Arts");

                FsmGameObject fsmgo = spellControl.GetAction<CreateObject>("Scream Burst 1", 2).gameObject;
                fsmgo.Value.gameObject.transform.position = new Vector3(0, 0, 0);
                fsmgo.Value.gameObject.transform.localPosition = new Vector3(0, -3, 0);
                spellControl.GetAction<CreateObject>("Scream Burst 1", 2).gameObject = fsmgo;

                //Note some of these repeats because after removing an action, their index is pushed backwards to fill in the missing parts
                spellControl.RemoveAction("Scream Burst 1", 6);  // Removes both Scream 1 "skulls"
                spellControl.RemoveAction("Scream Burst 1", 6);  // same

                spellControl.RemoveAction("Scream Burst 2", 7); //Same but for Scream 2
                spellControl.RemoveAction("Scream Burst 2", 7); //Same

                spellControl.RemoveAction("Level Check 2", 0); //removes the action that takes your soul when you slam

                spellControl.RemoveAction("Quake1 Land", 9); // Removes slam effect
                spellControl.RemoveAction("Quake1 Land", 11); // removes pillars

                spellControl.RemoveAction("Q2 Land", 11); //slam effects

                spellControl.RemoveAction("Q2 Pillar", 2); //pillars 
                spellControl.RemoveAction("Q2 Pillar", 2); // "Q mega" no idea but removing it otherwise

                spellControl.InsertAction("Can Cast?", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "SwapWeapon",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Can Cast? QC", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "CanCastQC",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Can Cast? QC", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "CanCastQC_SkipSpellReq",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 3);

                //Removes soul requirement
                //HeroController.instance.spellControl.RemoveAction("Can Cast? QC", 2);


                spellControl.AddAction("Quake Antic", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "StartQuake",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                );

                spellControl.AddAction("Quake1 Land", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "StartTyphoon",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                );

                spellControl.AddAction("Q2 Land", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "StartTyphoon",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                );

                spellControl.InsertAction("Has Fireball?", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "SpawnFireball",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Has Scream?", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "HasScream_HasFireSupportAmmo",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Has Quake?", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "HasQuake_CanCastQuake",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Scream End", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "ScreamEnd",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.InsertAction("Scream End 2", new CallMethod
                {
                    behaviour = GameManager.instance.GetComponent<SpellControlOverride>(),
                    methodName = "ScreamEnd",
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
                , 0);

                spellControl.RemoveAction("Scream Burst 1", 3);
                spellControl.RemoveAction("Scream Burst 2", 4);

                DontDestroyOnLoad(artifactActivatedEffect);

            }
            catch (Exception e)
            {
                Modding.Logger.Log(e);
            }

        }

        public void StartQuake()
        {
            //LoadAssets.sfxDictionary.TryGetValue("divetrigger.wav", out AudioClip ac);
            //AudioSource audios = HollowPointSprites.gunSpriteGO.GetComponent<AudioSource>();
            //audios.PlayOneShot(ac);
        }

        public void StartTyphoon()
        {
        }

        IEnumerator SpawnGasPulse(Vector3 spawnPos, float explosionAmount)
        {
            Modding.Logger.Log("Spawning Gas Pulse");
            float addedDegree = 180 / (explosionAmount + 1);
            AudioHandler.PlaySoundsMisc("divedetonate");
            GameObject dungCloud;
            for (int pulse = 0; pulse < 1; pulse++)
            {
                dungCloud = HollowPointPrefabs.SpawnObjectFromDictionary("Knight Spore Cloud", HeroController.instance.transform.position + new Vector3(0, 0, -.001f), Quaternion.identity);
                dungCloud.transform.localScale = new Vector3(1.75f, 1.75f, 0);
                dungCloud.GetComponent<DamageEffectTicker>().SetAttr<float>("damageInterval", 0.2f); //DEFAULT: 0.15
            }
            yield break;
        }

        public void SwapWeapon()
        {
 
            string animName = HeroController.instance.GetComponent<tk2dSpriteAnimator>().CurrentClip.name;
            if (animName.Contains("Sit") || animName.Contains("Get Off") || !HeroController.instance.CanCast()) return;

            if (!canSwap)
            {
                HeroController.instance.spellControl.SetState("Inactive");
                return;
            }

            swapTimer = (PlayerData.instance.equippedCharm_26)? 1.5f : 7f;

            HeroController.instance.spellControl.SetState("Inactive");
            Modding.Logger.Log("Swaping weapons");

            AudioSource audios = HollowPointSprites.gunSpriteGO.GetComponent<AudioSource>();
            if (WeaponSwapHandler.instance.currentWeapon == WeaponType.Ranged)
            {
                //Holster gun
                LoadAssets.sfxDictionary.TryGetValue("weapon_holster.wav", out AudioClip ac);
                audios.PlayOneShot(ac);

                /*the ACTUAL attack cool down variable, i did this to ensure the player wont have micro stutters 
                 * on animation because even at 0 animation time, sometimes they play for a quarter of a milisecond
                 * thus giving that weird head jerk anim playing on the knight
                */
                HeroController.instance.SetAttr<float>("attack_cooldown", 0.1f);
                WeaponSwapHandler.instance.SwapBetweenNail();
            }
            else
            {             
                //Equip gun
                LoadAssets.sfxDictionary.TryGetValue("weapon_draw.wav", out AudioClip ac);
                audios.PlayOneShot(ac);
                WeaponSwapHandler.instance.SwapBetweenNail();
            }
            isUsingGun = !isUsingGun;

            HeroController.instance.spellControl.SetState("Inactive");
        }

        public void CanCastQC_SkipSpellReq()
        {
            //HeroController.instance.spellControl.SetState("QC");
            //this stuff allows using spells on times youre NOT supposed to use it which is pretty busted
            //forgot why i wrote this, i think it was for preventing soul consumption but i already manually removed that
        }

        public void CanCastQC()
        {
            //Modding.Logger.Log("Forcing Fireball");
            //Modding.Logger.Log("[SpellControlOverride] Can Cast?  " + HeroController.instance.CanCast());
            if (!HeroController.instance.CanCast() || (PlayerData.instance.fireballLevel == 0))
            {
                spellControl.SetState("Inactive");
                return;
            }

            if ((WeaponSwapHandler.instance.currentWeapon == WeaponType.Ranged) && !(grenadeCooldown > 0))
            {
                grenadeCooldown = 30f;

                //StartCoroutine(SpreadShot(1));
                spellControl.SetState("Has Fireball?"); 
                spellControl.SetState("Inactive");

                //WeaponSwapHandler.SwapBetweenGun();
            }
            else if (WeaponSwapHandler.instance.currentWeapon == WeaponType.Ranged)
            {
                spellControl.SetState("Inactive");
            }
        }

        public void HasQuake_CanCastQuake()
        {
            if (!HeroController.instance.CanCast() || (PlayerData.instance.quakeLevel == 0)) return;

            int soulCost = (PlayerData.instance.equippedCharm_33) ? 24 : 33;

            if(PlayerData.instance.MPCharge < 33 || WeaponSwapHandler.instance.currentWeapon == WeaponType.Ranged)
            {       
                HeroController.instance.spellControl.SetState("Inactive");
            }
            else
            {
                HeroController.instance.TakeMP(soulCost);
            }
        }

        public void SpawnFireball()
        {
            HeroController.instance.spellControl.SetState("Inactive");

            int soulCost = (PlayerData.instance.equippedCharm_33) ? 22 : 33;
            if (WeaponSwapHandler.instance.currentWeapon == WeaponType.Melee || PlayerData.instance.fireballLevel == 0 || PlayerData.instance.MPCharge < soulCost)
            {
                HeroController.instance.spellControl.SetState("Inactive");
                return;
            }
            HeroController.instance.TakeMP(soulCost);
            StartCoroutine(BurstShot(5));
            HeroController.instance.spellControl.SetState("Spell End");
        }

        //========================================SECONDARY FIRE METHODS====================================


        public IEnumerator BurstShot(int burst)
        {
            GameCameras.instance.cameraShakeFSM.SendEvent("EnemyKillShake");

            for (int i = 0; i < burst; i++)
            {
                //HeatHandler.IncreaseHeat(20f);
                AudioHandler.PlayGunSounds("rifle");
                float direction = OrientationHandler.finalDegreeDirection;
                DirectionalOrientation orientation = OrientationHandler.directionOrientation;
                GameObject bullet = HollowPointPrefabs.SpawnBullet(direction, orientation);
                bullet.GetComponent<BulletBehaviour>().bulletSizeOverride = 1.6f;

                Destroy(bullet, .4f);

                HollowPointSprites.StartGunAnims();
                HollowPointSprites.StartFlash();
                HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);
                yield return new WaitForSeconds(0.07f); //0.12f This yield will determine the time inbetween shots   

                if (HeroController.instance.cState.dashing) break;
            }
        }

        public IEnumerator SpreadShot(int pellets)
        {
            GameCameras.instance.cameraShakeFSM.SendEvent("SmallShake"); //SmallShake
            HollowPointSprites.StartGunAnims();
            HollowPointSprites.StartFlash();
            HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);
            AudioHandler.PlayGunSounds("Shotgun");

            float direction = OrientationHandler.finalDegreeDirection; //90 degrees
            DirectionalOrientation orientation = OrientationHandler.directionOrientation;

            float coneDegree = 40;
            float angleToSpawnBullet = direction - (coneDegree / 2); //90 - (30 / 2) = 75, start at 75 degrees
            float angleIncreasePerPellet = coneDegree / (pellets + 2); // 30 / (5 + 2) = 4.3, move angle to fire for every pellet by 4.3 degrees

            angleToSpawnBullet = angleToSpawnBullet + angleIncreasePerPellet;

            //Checks if the player is firing upwards, and enables the x offset so the bullets spawns directly ontop of the knight
            //from the gun's barrel instead of spawning to the upper right/left of them 
            bool fixYOrientation = (direction == 270 || direction == 90) ? true : false;
            for (int i = 0; i < pellets; i++)
            {
                yield return new WaitForEndOfFrame();

                GameObject bullet = HollowPointPrefabs.SpawnBullet(angleToSpawnBullet, orientation);
                BulletBehaviour hpbb = bullet.GetComponent<BulletBehaviour>();
                hpbb.bulletDegreeDirection += UnityEngine.Random.Range(-3, 3);
                //hpbb.pierce = PlayerData.instance.equippedCharm_13;
                bullet.transform.localScale = new Vector3(0.2f, 0.2f, 0.1f);
                hpbb.specialAttrib = "Explosion";

                angleToSpawnBullet += angleIncreasePerPellet;
                Destroy(bullet, 0.7f);
            }

            yield return new WaitForSeconds(0.3f);
            AttackHandler.isFiring = false;
        }

        public void CheckNailArt()
        {
            //Modding.Logger.Log("Passing Through Nail Art");
            //nailArtFSM.SetState("Regain Control");
        }

        public void HasScream_HasFireSupportAmmo()
        {

            if (AttackHandler.airStrikeActive)
            {
                if(artifactActivatedEffect != null) spellControl.SetState("Inactive");
                artifactActivatedEffect.SetActive(false);
                AttackHandler.airStrikeActive = false;
                return;
            }

            //if (HP_Stats.artifactPower <= 0 || HP_WeaponHandler.currentGun.gunName != "Nail")
            if (PlayerData.instance.MPCharge < 99 || WeaponSwapHandler.instance.currentWeapon == WeaponType.Ranged)
            {
                spellControl.SetState("Inactive");
            }
        }

        public void ScreamEnd()
        {
            //Prepare the airstrike by taking 99 MP
            HeroController.instance.TakeMP(99);
            StartCoroutine(StartSteelRainNoTrack(HeroController.instance.transform.position, 8));

            //artifactActivatedEffect = Instantiate(HeroController.instance.artChargeEffect, HeroController.instance.transform);
            //artifactActivatedEffect.SetActive(true);
            //AttackHandler.airStrikeActive = true;
            //infuseTimer = 500f;
        }

        //========================================FIRE SUPPORT SPAWN METHODS====================================

        //Regular steel rain (non tracking)
        public static IEnumerator StartSteelRainNoTrack(Vector3 targetCoordinates, int totalShells)
        {
            int artyDirection = (HeroController.instance.cState.facingRight) ? 1 : -1;
            Modding.Logger.Log("SPELL CONTROL STEEL RAIN NO TRACKING");
            float shellAimPosition = 5 * artyDirection; //Allows the shell to "walk" slowly infront of the player
            for (int shells = 0; shells < totalShells; shells++)
            {
                //GameObject shell = Instantiate(HollowPointPrefabs.bulletPrefab, targetCoordinates + new Vector3(Range(-5, 5), Range(25, 50), -0.1f), new Quaternion(0, 0, 0, 0));
                GameObject shell = HollowPointPrefabs.SpawnBulletAtCoordinate(270, HeroController.instance.transform.position + new Vector3(shellAimPosition, 30, -0.1f), 0);
                shellAimPosition += 3 * artyDirection;
                BulletBehaviour hpbb = shell.GetComponent<BulletBehaviour>();
                hpbb.isFireSupportBullet = true;
                hpbb.ignoreCollisions = true;
                hpbb.targetDestination = targetCoordinates + new Vector3(0, Range(3f,6f), -0.1f);
                shell.SetActive(true);
                yield return new WaitForSeconds(0.5f);
            }
        }

        public static IEnumerator StartInfusion()
        {
            
            artifactActivatedEffect.SetActive(false);
            LoadAssets.sfxDictionary.TryGetValue("infusionsound.wav", out AudioClip ac);
            AudioSource aud = infusionSoundGO.GetComponent<AudioSource>();
            aud.PlayOneShot(ac);
            buffActive = true;
            if (PlayerData.instance.equippedCharm_34)
            {
                buff_duration = -40f;
                HeroController.instance.AddHealth(4);
            }

            //To make sure the minimum buff duration is always at 30f
            buff_duration = (buff_duration < 30f) ? 30f : buff_duration;

            GameCameras.instance.cameraShakeFSM.SendEvent("BigShake");

            //Gives fancy effects to when you infuse yourself, should add a sound soon
            Instantiate(sharpFlash, HeroController.instance.transform).SetActive(true);
            Instantiate(focusBurstAnim, HeroController.instance.transform).SetActive(true);

            SpriteFlash knightFlash = HeroController.instance.GetAttr<SpriteFlash>("spriteFlash");
            knightFlash.flashBenchRest();

            GameObject artChargeEffect = Instantiate(HeroController.instance.artChargedEffect, HeroController.instance.transform.position, Quaternion.identity);
            artChargeEffect.SetActive(true);
            artChargeEffect.transform.SetParent(HeroController.instance.transform);
            Destroy(artChargeEffect, buff_duration/10f);

            GameObject artChargeFlash = Instantiate(HeroController.instance.artChargedFlash, HeroController.instance.transform.position, Quaternion.identity);
            artChargeFlash.SetActive(true);
            artChargeFlash.transform.SetParent(HeroController.instance.transform);
            Destroy(artChargeFlash, 0.5f);

            GameObject dJumpFlash = Instantiate(HeroController.instance.dJumpFlashPrefab, HeroController.instance.transform.position, Quaternion.identity);
            dJumpFlash.SetActive(true);
            dJumpFlash.transform.SetParent(HeroController.instance.transform);
            Destroy(dJumpFlash, 0.5f);

            yield return null;
        }



        public static void PlayAudio(string audioName, bool addPitch)
        {
            LoadAssets.sfxDictionary.TryGetValue(audioName.ToLower() + ".wav", out AudioClip ac);
            AudioSource audios = HeroController.instance.spellControl.GetComponent<AudioSource>();
            audios.clip = ac;
            audios.pitch = 1;
            //HP_Sprites.gunSpriteGO.GetComponent<AudioSource>().PlayOneShot(ac);
            if (addPitch)
                audios.pitch = Range(0.8f, 1.5f);

            audios.PlayOneShot(audios.clip);
        }

        void OnDestroy()
        {
            Destroy(gameObject.GetComponent<SpellControlOverride>());
            Modding.Logger.Log("SpellControl Destroyed");
        }
    }

}
