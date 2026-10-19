using System;
using System.Collections.Generic;
using System.IO;
using FPLab.Gameplay.Maze;

public static class InspectLayout
{
    private static int[] dx = {0,1,0,-1}, dy = {1,0,-1,0};
    private static MazeWall[] walls = {MazeWall.North,MazeWall.East,MazeWall.South,MazeWall.West};
    public static object Run()
    {
        int min = int.MaxValue, max = 0, total = 0, minLeaves = int.MaxValue;
        var issues = new List<string>();
        var samples = new List<object>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for(int seed=-8; seed<24;seed++)
        {
            MazeData data = MazeGenerator.Generate(33,33,seed,150);
            var distance = new int[33*33];
            Array.Fill(distance,-1);
            var queue = new Queue<int>(); queue.Enqueue(0); distance[0]=0;
            int reached = 0, leaves = 0;
            while(queue.Count>0)
            {
                int cell=queue.Dequeue(), x=cell%33,y=cell/33,open=0; reached++;
                for(int d=0;d<4;d++)
                {
                    int nx=x+dx[d],ny=y+dy[d];
                    bool wall=data[x,y].HasWall(walls[d]);
                    if(nx<0||ny<0||nx>=33||ny>=33)
                    { if(!wall) issues.Add(seed+": open exterior"); continue; }
                    if(wall != data[nx,ny].HasWall(walls[(d+2)%4])) issues.Add(seed+": reciprocal wall");
                    if(wall) continue;
                    if(!data.IsWalkable(nx,ny)) issues.Add(seed+": passage to solid");
                    open++;
                    int next=ny*33+nx;
                    if(distance[next]>=0)continue;
                    distance[next]=distance[cell]+1;queue.Enqueue(next);
                }
                if(open==1)leaves++;
            }
            if(reached!=data.WalkableCellCount)issues.Add(seed+": unreachable cells");
            if(data.ExitDistance!=distance[data.ExitY*33+data.ExitX])issues.Add(seed+": exit distance mismatch");
            if(data.ExitDistance<150)issues.Add(seed+": route below 330m: "+data.ExitDistance*2.2f);
            min=Math.Min(min,data.ExitDistance);max=Math.Max(max,data.ExitDistance);total+=data.ExitDistance;minLeaves=Math.Min(minLeaves,leaves);
            samples.Add(new {seed,steps=data.ExitDistance,metres=data.ExitDistance*2.2f,cells=data.WalkableCellCount,leaves,rooms=data.Rooms.Count});
            if(seed==0)
            {
                var cells=new List<object>();
                for(int y=0;y<33;y++) for(int x=0;x<33;x++)
                    cells.Add(new {x,y,kind=data.GetCellKind(x,y).ToString(),section=data.GetSectionId(x,y),walls=(int)data[x,y].Walls,distance=distance[y*33+x]});
                File.WriteAllText("Tools/Art/EntranceLayout/Plan.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {width=33,height=33,cellSize=2.2f,exitX=data.ExitX,exitY=data.ExitY,cells}));
            }
        }
        clock.Stop();
        var report = new {seeds=samples.Count,minMetres=min*2.2f,maxMetres=max*2.2f,meanMetres=total*2.2f/samples.Count,minDeadEnds=minLeaves,elapsedMs=clock.ElapsedMilliseconds,issues,samples};
        File.WriteAllText("Tools/Art/EntranceLayout/LayoutAnalysis.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
