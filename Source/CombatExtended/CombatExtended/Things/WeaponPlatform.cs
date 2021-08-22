﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CombatExtended
{
    public class WeaponPlatform : ThingWithComps
    {
        private const int VERB_EXPIRE_TICKS = 600;
        
        public readonly List<AttachmentLink> attachments = new List<AttachmentLink>();

        private Verb altVerb;       
        private int altVerbExpireAt = -1;        
        private Quaternion drawQat;
        private List<WeaponPlatformDef.WeaponGraphicPart> _defaultPart = new List<WeaponPlatformDef.WeaponGraphicPart>();
        
        private List<AttachmentDef> _additionList = new List<AttachmentDef>();
        private List<AttachmentDef> _removalList = new List<AttachmentDef>();
        private List<AttachmentDef> _targetConfig = new List<AttachmentDef>();

        /// <summary>
        /// The config that this weapon should have. Used for billing.
        /// </summary>
        public List<AttachmentDef> TargetConfig
        {
            get
            {                
                return _targetConfig.ToList();
            }
            set
            {
                _targetConfig = value;
                UpdateConfiguration();
            }
        }      

        private AttachmentLink[] _curLinks;
        /// <summary>
        /// Return the current attachments links
        /// </summary>
        public AttachmentLink[] CurLinks
        {
            get
            {
                if (_curLinks == null || _curLinks.Length != attachments.Count)
                    _curLinks = attachments.ToArray();
                return _curLinks;
            }
        }       

        /// <summary>
        /// Wether the target config match the current loadout
        /// </summary>
        public bool ConfigApplied
        {
            get
            {                                
                return _additionList.Count == 0 && _removalList.Count == 0;
            }
        }      

        /// <summary>
        /// The wielder pawn
        /// </summary>
        public Pawn Wielder
        {
            get
            {
                if (false
                    || Equippable == null
                    || Equippable.PrimaryVerb == null
                    || Equippable.PrimaryVerb.caster == null
                    || ((Equippable?.parent?.ParentHolder as Pawn_InteractionsTracker)?.pawn is Pawn holderPawn && holderPawn != Equippable?.PrimaryVerb?.CasterPawn))                
                    return null;                
                return Equippable.PrimaryVerb.CasterPawn;
            }
        }

        /// <summary>
        /// Attachments that need to be removed
        /// </summary>
        public List<AttachmentDef> RemovalList
        {
            get
            {
                return _removalList;
            }
        }

        /// <summary>
        /// Attachments that need to be added
        /// </summary>
        public List<AttachmentDef> AdditionList
        {
            get
            {
                return _additionList;
            }
        }

        /// <summary>
        /// Return the current visible WeaponGraphicParts.
        /// </summary>
        public List<WeaponPlatformDef.WeaponGraphicPart> VisibleDefaultParts
        {
            get
            {
                if (_defaultPart == null)
                    _defaultPart = new List<WeaponPlatformDef.WeaponGraphicPart>();
                return _defaultPart;
            }
        }
      
        private WeaponPlatformDef _platformDef;
        public WeaponPlatformDef Platform
        {
            get
            {
                if (_platformDef == null)
                    _platformDef = (WeaponPlatformDef)def;
                return _platformDef;
            }
        }

        private CompEquippable _equippable;
        public CompEquippable Equippable
        {
            get
            {
                if (_equippable == null)
                    _equippable = GetComp<CompEquippable>();
                return _equippable;
            }
        }

        private Dictionary<AttachmentDef, AttachmentLink> _LinkByDef = new Dictionary<AttachmentDef, AttachmentLink>();
        public Dictionary<AttachmentDef, AttachmentLink> LinkByDef
        {
            get
            {
                if(_LinkByDef.Count != Platform.attachmentLinks.Count)
                {
                    _LinkByDef.Clear();
                    foreach (AttachmentLink link in Platform.attachmentLinks)
                        _LinkByDef.Add(link.attachment, link);
                }
                return _LinkByDef;
            }
        }

        /// <summary>
        /// Return the original Primary verb of this weapon
        /// </summary>
        public Verb PrimaryVerb
        {
            get
            {
                VerbTracker tracker = Equippable.verbTracker;
                if (tracker.verbs == null)
                {
                    tracker.InitVerbsFromZero();
                }
                for (int i = 0; i < tracker.verbs.Count; i++)
                {
                    if (tracker.verbs[i].verbProps.isPrimary)
                    {
                        return tracker.verbs[i];
                    }
                }
                return null;
            }
        }
       

        /// <summary>
        /// Return the current selected verb or the default verb for this weapon.
        /// </summary>
        public Verb SelectedVerb
        {
            get
            {               
                Verb verb = altVerb;
                if (verb == null)
                    return PrimaryVerb;
                if (GenTicks.TicksGame > altVerbExpireAt)
                    return verb = null;
                return verb;
            }
            set
            {
                if (value == PrimaryVerb)
                {
                    altVerb = null;
                    altVerbExpireAt = -1;
                }
                else
                {                    
                    altVerb = value;
                    altVerbExpireAt = GenTicks.TicksGame + VERB_EXPIRE_TICKS;
                }
            }
        }

        public WeaponPlatform()
        {                        
        }        

        public override void ExposeData()
        {
            base.ExposeData();                      
            // start - scribe the current attachments
            List<AttachmentDef> defs = this.attachments.Select(l => l.attachment).ToList();            
            Scribe_Collections.Look(ref defs, "attachments", LookMode.Def);
            if(Scribe.mode != LoadSaveMode.Saving && defs != null)
            {
                attachments.Clear();
                attachments.AddRange(defs.Select(a => Platform.attachmentLinks.First(l => l.attachment == a)));
            }
            // scribe the remaining content
            Scribe_Collections.Look(ref _additionList, "additionList", LookMode.Def);
            if (_additionList == null)
                _additionList = new List<AttachmentDef>();
            Scribe_Collections.Look(ref _removalList, "removalList", LookMode.Def);
            if (_removalList == null)
                _removalList = new List<AttachmentDef>();
            // scribe the current config
            Scribe_Collections.Look(ref this._targetConfig, "targetConfig", LookMode.Def);
            if (this._targetConfig == null)
                this._targetConfig = new List<AttachmentDef>();
            if (Scribe.mode != LoadSaveMode.Saving)
                UpdateConfiguration();            
        }

        /// <summary>
        /// Return the attachments we need to either remove or add
        /// </summary>
        /// <returns></returns>
        public IEnumerable<AttachmentDef> GetModificationList()
        {
            return AdditionList.Concat(RemovalList);            
        }                
         
        public override void PostPostMake()
        {
            base.PostPostMake();
            this.UpdateConfiguration();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // create the random angle for when sat on the ground.
            float angle = Rand.Range(-Platform.graphicData.onGroundRandomRotateAngle, Platform.graphicData.onGroundRandomRotateAngle) % 180;
            this.drawQat = angle.ToQuat();
            this.UpdateConfiguration();
        }

        public override IEnumerable<InspectTabBase> GetInspectTabs()
        {
            List<InspectTabBase> tabs = base.GetInspectTabs()?.ToList() ?? new List<InspectTabBase>();
            // check if our tab is not in the inspectTabs
            if (!tabs.Any(t => t is ITab_AttachmentView))
                tabs.Add(InspectTabManager.GetSharedInstance(typeof(ITab_AttachmentView)));
            return tabs;
        }

        private Matrix4x4 _drawMat;
        private Vector3 _drawLoc;

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // Check draw matrix cache
            if (_drawLoc.x != drawLoc.x || _drawLoc.z != drawLoc.z)
            {
                _drawMat = new Matrix4x4();
                _drawMat.SetTRS(drawLoc, drawQat, Vector3.one);
                _drawLoc = drawLoc;
            }            
            DrawPlatform(_drawMat, false);
        }

        /// <summary>
        /// Get attachment link for a given attachment def
        /// </summary>
        /// <param name="def">Attachment def</param>
        /// <returns>Said attachment link</returns>
        public AttachmentLink GetLink(AttachmentDef def)
        {
            return LinkByDef.TryGetValue(def, out var link) ? link : null;
        }

        private List<Pair<Material, Mesh>> _graphicCache;
        private List<Pair<Material, Mesh>> _graphicFlipCache;       

        /// <summary>
        /// Used to render the actual weapon.
        /// </summary>
        /// <param name="matrix">Matrix4x4</param>
        /// <param name="flip">Flip</param>
        /// <param name="layer">Layer</param>
        public void DrawPlatform(Matrix4x4 matrix, bool flip = false, int layer = 0)
        {
            if (_graphicCache == null)
                UpdateDrawCache();
            List<Pair<Material, Mesh>> cache = !flip ? _graphicCache : _graphicFlipCache;
            for (int i = 0; i < cache.Count; i++)
            {
                Pair<Material, Mesh> part = cache[i];
                Graphics.DrawMesh(part.Second, matrix, part.First, layer);
            }
        }

        /// <summary>
        /// Used to update the internel config lists
        /// </summary>
        public void UpdateConfiguration()
        {            
            _removalList.Clear();
            _additionList.Clear();
            /*
             * <=========   attachments =========> 
             */
            _curLinks = attachments.Select(t => LinkByDef[t.attachment]).ToArray();

            foreach (AttachmentLink link in Platform.attachmentLinks)
            {
                AttachmentDef def = link.attachment;
                bool inConfig = _targetConfig.Any(d => d.index == def.index);
                bool inContainer = attachments.Any(l => l.attachment.index == def.index);
                if (inConfig && !inContainer)                
                    _additionList.Add(def);                    
                else if (!inConfig && inContainer)
                    _removalList.Add(def);                                    
            }
            /*
             * <========= default parts =========> 
             */
            _defaultPart.Clear();
            foreach (WeaponPlatformDef.WeaponGraphicPart part in Platform.defaultGraphicParts)
            {
                /*
                 * We add default parts that enable use to change the graphics of the weapon
                 */
                if (part.slotTags == null || part.slotTags.All(s => _curLinks.All(l => !l.attachment.slotTags.Contains(s))))                
                    _defaultPart.Add(part);
            }
            /*
             * <========= graphic cache =========> 
             */
            _graphicCache = null;
            _graphicFlipCache = null;
        }               

        /// <summary>
        /// Used to rebuild rendering internel cache
        /// </summary>
        public void UpdateDrawCache()
        {
            _graphicCache ??= new List<Pair<Material, Mesh>>();
            _graphicCache.Clear();
            _graphicFlipCache ??= new List<Pair<Material, Mesh>>();
            _graphicFlipCache.Clear();
            for (int i = 0; i < _defaultPart.Count; i++)
            {
                WeaponPlatformDef.WeaponGraphicPart part = _defaultPart[i];
                if (part.HasOutline)
                {
                    _graphicCache.Add(new Pair<Material, Mesh>(part.OutlineMat, CE_MeshMaker.plane10Bot));
                    _graphicFlipCache.Add(new Pair<Material, Mesh>(part.OutlineMat, CE_MeshMaker.plane10FlipBot));
                }
            }
            for (int i = 0; i < _curLinks.Length; i++)
            {
                AttachmentLink link = _curLinks[i];
                if (link.HasOutline)
                {
                    _graphicCache.Add(new Pair<Material, Mesh>(link.OutlineMat, link.meshBot));
                    _graphicFlipCache.Add(new Pair<Material, Mesh>(link.OutlineMat, link.meshFlipBot));
                }
            }
            _graphicCache.Add(new Pair<Material, Mesh>(Platform.graphic.MatSingle, CE_MeshMaker.plane10Mid));
            _graphicFlipCache.Add(new Pair<Material, Mesh>(Platform.graphic.MatSingle, CE_MeshMaker.plane10FlipMid));

            for (int i = 0; i < _defaultPart.Count; i++)
            {
                WeaponPlatformDef.WeaponGraphicPart part = _defaultPart[i];
                if (part.HasPartMat)
                {
                    _graphicCache.Add(new Pair<Material, Mesh>(part.PartMat, CE_MeshMaker.plane10Top));
                    _graphicFlipCache.Add(new Pair<Material, Mesh>(part.PartMat, CE_MeshMaker.plane10FlipTop));
                }
            }
            for (int i = 0; i < _curLinks.Length; i++)
            {
                AttachmentLink link = _curLinks[i];
                if (link.HasAttachmentMat)
                {
                    _graphicCache.Add(new Pair<Material, Mesh>(link.AttachmentMat, link.meshTop));
                    _graphicFlipCache.Add(new Pair<Material, Mesh>(link.AttachmentMat, link.meshFlipTop));
                }
            }
        }

        /// <summary>
        /// return the edit gzimo if this weapon is in storage and not on some pawn
        /// </summary>
        /// <returns></returns>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;
            if (this.Spawned)
                yield return new GizmoEditAttachments() { weapon = this };
        }
    }
}
