﻿using System;
using System.Collections.Generic;

namespace AWBWApp.Game.API.Replay
{
    public class ReplayUser
    {
        public int ID;
        public int UserId;
        public string Username; //This needs to be filled in by asking AWBW

        public string TeamName;
        public int CountryId;

        public HashSet<int> COsUsedByPlayer = new HashSet<int>();

        public int ReplayIndex;
        public int RoundOrder;
        public int? EliminatedOn;
    }

    public class AWBWReplayPlayerTurn
    {
        public int ID; //Just to make sure things stay the same
        public int Funds;

        public int ActiveCOID;
        public int Power;
        public int? RequiredPowerForNormal; //This changes every time we use a power.
        public int? RequiredPowerForSuper; //This changes every time we use a power.

        public int? TagCOID;
        public int? TagPower;
        public int? TagRequiredPowerForNormal; //This changes every time we use a power.
        public int? TagRequiredPowerForSuper; //This changes every time we use a power.

        public ActiveCOPowers ActiveCOPowers;
        public bool Eliminated;
    }

    [Flags]
    public enum ActiveCOPowers
    {
        None = 0,
        Normal = 1 << 0,
        Super = 1 << 1,
        TagNormal = 1 << 2,
        TagSuper = 1 << 3,
    }
}