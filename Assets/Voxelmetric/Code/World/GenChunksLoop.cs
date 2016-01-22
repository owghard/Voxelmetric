﻿using System.Collections;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// This class runs constantly running generation jobs for chunks. When chunks are added to one of the
/// generation stages (chunk.generationStage) it should also be added to a list here and this 
/// class will work through every list running the job for the relevant stage and pushing it to the
/// next stage. Chunks can be added and forgotten because they will work their way to fully functioning
/// chunks by the end.
/// 
/// Use ChunksInProgress to check the number of chunks in queue to be generated before adding a new one.
/// There's no point in piling up the queue, better to wait, then add more.
/// </summary>
public class ChunksLoop {

    Dictionary<Stage, List<BlockPos>> chunkWorkLists = new Dictionary<Stage, List<BlockPos>>();
    List<BlockPos> markedForDeletion = new List<BlockPos>();
    List<BlockPos> renderMesh = new List<BlockPos>();
    World world;

    public bool isPlaying = true;
    public Thread loopThread;
    public Thread renderThread;

    public ChunksLoop(World world)
    {
        this.world = world;
        chunkWorkLists.Add(Stage.terrain, new List<BlockPos>());
        chunkWorkLists.Add(Stage.buildMesh, new List<BlockPos>());
        chunkWorkLists.Add(Stage.saveAndDelete, new List<BlockPos>());
        chunkWorkLists.Add(Stage.delete, new List<BlockPos>());

        loopThread = new Thread(() =>
        {
            while (isPlaying)
            {
                try
                {
                    Terrain();
                }
                catch (Exception ex)
                {
                    Debug.Log(ex);
                }
            }
        });

        renderThread = new Thread(() =>
       {
           while (isPlaying)
           {
               try
               {
                   BuildMesh();
               }
               catch (Exception ex)
               {
                   Debug.Log(ex);
               }
           }
       });

        loopThread.Start();
        renderThread.Start();
    }

    public int ChunksInProgress
    {
        get
        {
            int i = chunkWorkLists[Stage.buildMesh].Count;
            i += chunkWorkLists[Stage.terrain].Count;
            return i;
        }
    }

    public void MainThreadLoop()
    {
        DeleteMarkedChunks();
        UpdateMeshFilters();
        // eventually we want to handle adding mesh data to the unity mesh
        // and maybe even the chunk's regular update func
    }

    void Terrain()
    {
        int index = 0;

        while (chunkWorkLists[Stage.terrain].Count > index)
        {
            Chunk chunk = world.chunks.Get(chunkWorkLists[Stage.terrain][index]);

            if (!IsCorrectStage(Stage.terrain, chunk))
            {
                chunkWorkLists[Stage.terrain].RemoveAt(index);
                continue;
            }

            bool chunksAllGenerated = true;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Chunk chunkToGen = world.chunks.Get(chunk.pos + (new BlockPos(x, y, z) * Config.Env.ChunkSize));
                        if (chunkToGen)
                        {
                            chunkToGen.blocks.GenerateChunkContents();

                            if (!chunkToGen.blocks.contentsGenerated)
                            {
                                return;
                            }
                            //    while (!chunkToGen.blocks.contentsGenerated)
                            //{
                            //    Thread.Sleep(0);
                            //    //chunksAllGenerated = false;
                            //}
                            
                        }
                    }
                }
            }

            if (chunksAllGenerated)
            {
                chunk.stage = Stage.buildMesh;
            }
            else
            {
                index++;
            }
        }
    }

    void BuildMesh()
    {
        while (chunkWorkLists[Stage.buildMesh].Count > 0)
        {
            Chunk chunk = world.chunks.Get(chunkWorkLists[Stage.buildMesh][0]);
            if (!IsCorrectStage(Stage.buildMesh, chunk))
            {
                chunkWorkLists[Stage.buildMesh].RemoveAt(0);
                continue;
            }

            chunk.render.BuildMeshData();
            chunk.stage = Stage.ready;
            renderMesh.Add(chunk.pos);
        }
    }

    void UpdateMeshFilters()
    {
        int index = 0;
        while (renderMesh.Count > index)
        {
            Chunk chunk = world.chunks.Get(renderMesh[index]);

            if (chunk == null)
            {
                index++;
                continue;
            }

            chunk.logic.SetFlag(Flag.meshReady, false);
            chunk.render.RenderMesh();
            chunk.render.ClearMeshData();

            renderMesh.RemoveAt(index);
            chunk.logic.SetFlag(Flag.busy, false);
        }
    }

    void DeleteMarkedChunks()
    {
        int index = 0;
        while (markedForDeletion.Count > index)
        {
            Chunk chunk = world.chunks.Get(markedForDeletion[index]);

            if (chunk == null)
            {
                markedForDeletion.RemoveAt(index);
            }

            if (chunk.blocks.contentsGenerated && (chunk.stage == Stage.created || chunk.stage == Stage.ready))
            {
                if (chunk.logic.GetFlag(Flag.chunkModified))
                {
                    Serialization.SaveChunk(chunk);
                }

                chunk.ReturnChunkToPool();
                markedForDeletion.RemoveAt(index);
            }
            else
            {
                index++;
                continue;
            }
        }
    }

    public void AddToDeletionList(Chunk chunk)
    {
        if (!markedForDeletion.Contains(chunk.pos))
        {
            markedForDeletion.Add(chunk.pos);
        }
    }

    public void ChunkStageChanged(Chunk chunk, Stage oldStage, Stage newStage)
    {
        if (chunkWorkLists.ContainsKey(oldStage) &&
            chunkWorkLists[oldStage].Contains(chunk.pos))
            chunkWorkLists[oldStage].Remove(chunk.pos);

        if (chunkWorkLists.ContainsKey(newStage) &&
            !chunkWorkLists[newStage].Contains(chunk.pos))
            chunkWorkLists[newStage].Add(chunk.pos);
    }

    bool IsCorrectStage(Stage stage, Chunk chunk)
    {
        return (chunk != null && chunk.stage == stage);
    }
}