using UnityEngine;
using System;
using GlimmerDiary.Flora;

public class TreeConfigData
{
    public Vector3 location;
    public float rotation;
    public float sizeScale;
    public int seed;
    public long birthTimestamp;
    public string definitionID;
    public PlantDefinition plantDefinition;


    public TreeConfigData(Vector3 location, float rotation, float sizeScale, int seed, long birthTimestamp, string definitionID, PlantDefinition plantDefinition)
    {
        this.location = location;
        this.rotation = rotation;
        this.sizeScale = sizeScale;
        this.seed = seed;
        this.birthTimestamp = birthTimestamp;
        this.definitionID = definitionID;
        this.plantDefinition = plantDefinition;
    }
}