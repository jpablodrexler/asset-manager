﻿using JPPhotoManager.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPPhotoManager.Test
{
    internal class MockAssetHashCalculator : IAssetHashCalculatorService
    {
        public string CalculateHash(byte[] imageBytes)
        {
            return "abcd1234";
        }
    }
}
