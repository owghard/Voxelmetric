﻿using UnityEngine;
using Voxelmetric.Code.Core;
using Voxelmetric.Code.Load_Resources;
using Voxelmetric.Code.Utilities;
using Voxelmetric.Code.Utilities.Noise;

public class AdditiveLayer: TerrainLayer
{
    private Block blockToPlace;
    private int minHeight;
    private int maxHeight;
    private int amplitude;

    protected override void SetUp(LayerConfig config)
    {
        // Config files for additive layers MUST define these properties
        blockToPlace = world.blockProvider.GetBlock(properties["blockName"]);

        if (properties.ContainsKey("blockColors"))
        {
            string[] colors = properties["blockColors"].Split(',');
            ((ColoredBlock)blockToPlace).color = new Color(byte.Parse(colors[0]) / 255f, byte.Parse(colors[1]) / 255f, byte.Parse(colors[2]) / 255f);
        }

        noise.Frequency = 1f/float.Parse(properties["frequency"]); // Frequency in configs is in fast 1/frequency
        noise.Gain = float.Parse(properties["exponent"]);
#if UNITY_STANDALONE_WIN && !DISABLE_FASTSIMD
        noiseSIMD.Frequency = noise.Frequency;
        noiseSIMD.Gain = noise.Gain;
#endif
        minHeight = int.Parse(properties["minHeight"]);
        maxHeight = int.Parse(properties["maxHeight"]);

        amplitude = maxHeight - minHeight;
    }

    public override void PreProcess(Chunk chunk, int layerIndex)
    {
        NoiseItem ni = chunk.pools.noiseItems[layerIndex];
        ni.noiseGen.SetInterpBitStep(Env.ChunkSize, 2);
        ni.lookupTable = chunk.pools.FloatArrayPool.Pop(ni.noiseGen.Size * ni.noiseGen.Size);

#if UNITY_STANDALONE_WIN && !DISABLE_FASTSIMD
        float[] noiseSet = chunk.pools.FloatArray(ni.noiseGen.Size * ni.noiseGen.Size * ni.noiseGen.Size);

        // Generate SIMD noise
        int offsetShift = Env.ChunkPow - ni.noiseGen.Step;
        int xStart = (chunk.pos.x >> Env.ChunkPow) << offsetShift;
        int yStart = (chunk.pos.y >> Env.ChunkPow) << offsetShift;
        int zStart = (chunk.pos.z >> Env.ChunkPow) << offsetShift;
        float scaleModifier = 1 << ni.noiseGen.Step;
        noiseSIMD.Noise.FillNoiseSet(noiseSet, xStart, yStart, zStart, ni.noiseGen.Size, ni.noiseGen.Size, ni.noiseGen.Size, scaleModifier);

        // Generate a lookup table
        int i = 0;
        for (int z = 0; z < ni.noiseGen.Size; z++)
            for (int x = 0; x < ni.noiseGen.Size; x++)
                ni.lookupTable[i++] = NoiseUtilsSIMD.GetNoise(noiseSet, ni.noiseGen.Size, x, 0, z, amplitude, noise.Gain);

        chunk.pools.FloatArray(noiseSet);
#else
        int xOffset = chunk.pos.x;
        int zOffset = chunk.pos.z;

        // Generate a lookup table
        int i = 0;
        for (int z = 0; z < ni.noiseGen.Size; z++)
        {
            float zf = (z << ni.noiseGen.Step) + zOffset;

            for (int x = 0; x < ni.noiseGen.Size; x++)
            {
                float xf = (x << ni.noiseGen.Step) + xOffset;
                ni.lookupTable[i++] = NoiseUtils.GetNoise(noise.Noise, xf, 0, zf, 1f, amplitude, noise.Gain);
            }
        }
#endif
    }

    public override void PostProcess(Chunk chunk, int layerIndex)
    {
        NoiseItem ni = chunk.pools.noiseItems[layerIndex];
        chunk.pools.FloatArrayPool.Push(ni.lookupTable);
    }

    public override float GetHeight(Chunk chunk, int layerIndex, int x, int z, float heightSoFar, float strength)
    {
        NoiseItem ni = chunk.pools.noiseItems[layerIndex];

        // Calculate height to add and sum it with the min height (because the height of this
        // layer should fluctuate between minHeight and minHeight+the max noise) and multiply
        // it by strength so that a fraction of the result that gets used can be decided
        float heightToAdd = ni.noiseGen.Interpolate(x, z, ni.lookupTable);
        heightToAdd += minHeight;
        heightToAdd = heightToAdd * strength;

        return heightSoFar + heightToAdd;
    }

    public override float GenerateLayer(Chunk chunk, int layerIndex, int x, int z, float heightSoFar, float strength)
    {
        NoiseItem ni = chunk.pools.noiseItems[layerIndex];

        // Calculate height to add and sum it with the min height (because the height of this
        // layer should fluctuate between minHeight and minHeight+the max noise) and multiply
        // it by strength so that a fraction of the result that gets used can be decided
        float heightToAdd = ni.noiseGen.Interpolate(x, z, ni.lookupTable);
        heightToAdd += minHeight;
        heightToAdd = heightToAdd * strength;

        SetBlocks(chunk, x, z, (int)heightSoFar, (int)(heightSoFar + heightToAdd), blockToPlace);

        return heightSoFar + heightToAdd;
    }
}
