using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using Random = UnityEngine.Random;

public class Board : MonoBehaviour
{
    public class BubbleSlot
    {
        public Bubble bubble;
        public int generation = 0;
    }

    // hàm xử lý va chạm bóng
    public class BubbleCollision
    {
        public Vector2 grid;
        public bool isSticking;  // ktra có gắn với nhau không
        public Vector2 collidingPointNormal; // điểm va chạm
    }

    public Bubble.BubbleType[] bubbleTypes;

    public int bubblesPerRow = 6;  // số bóng trên 1 dòng
    public int boardHeightInBubbleRows = 10;
    public float bubbleRadius = 0.5f;
    public float bubbleShootingSpeed = 3f; // tốc độ bắn
    public int bubbleChainThreshold = 3; // số bóng cùng màu 
    public float dropTo = -5f;
    public float dropSpeed = 15f;  // tốc độ rơi

    public GameObject canon;

    public Bubble bubblePrefab;

    private int currentBubbleGeneration = 0;

    private BubbleSlot[] slots;
    private float hexagonSize;
    private float leftBoarder = 0.0f;
    private float rightBoarder;
    private float topBoarder = 0.0f;
    private float bottomBoarder;

    private float collideThreshold = 0.95f; // ngưỡng va chạm

    private bool canShoot = true;
    private Bubble nextBubble;
    private Bubble currentBubble;

    private Vector2[] neighbourGridOffsetsForEvenRow
        = { new Vector2(-1, 0), new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 1) };
    private Vector2[] neighbourGridOffsetsForOddRow
        = { new Vector2(-1, 0), new Vector2(0, -1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

    private void FloodFill(int col, int row, Predicate<BubbleSlot> should_process, Action<BubbleSlot> process)
    {
        if (col < 0 || col >= bubblesPerRow || row < 0 || row >= boardHeightInBubbleRows) return;

        var slot_idx = col + row * bubblesPerRow;

        if (!should_process(slots[slot_idx])) return;

        process(slots[slot_idx]);

        var neighbour_grid_offsets = (row & 1) == 1 ? neighbourGridOffsetsForOddRow : neighbourGridOffsetsForEvenRow;
        foreach (var neighbour_grid_offset in neighbour_grid_offsets)
        {
            FloodFill(col + (int)neighbour_grid_offset.x, row + (int)neighbour_grid_offset.y, should_process, process);
        }
    }

    // Use this for initialization
    void Start()
    {
        slots = new BubbleSlot[bubblesPerRow * boardHeightInBubbleRows];
        for (var i = 0; i < slots.Length; ++i)
        {
            slots[i] = new BubbleSlot();
        }

        hexagonSize = bubbleRadius / Mathf.Cos(Mathf.Deg2Rad * 30.0f);
        var hexagonHeight = hexagonSize * 2;
        var hexagonVerticalDistance = hexagonHeight * 3 / 4;

        rightBoarder = leftBoarder + bubbleRadius * bubblesPerRow * 2 + bubbleRadius;
        bottomBoarder = topBoarder - (boardHeightInBubbleRows - 1) * hexagonVerticalDistance - hexagonSize * 2;

        LoadBubbles();

        ArrangeBubblesInSlots();

        ReloadCanon();
    }

    private void LoadBubbles()
    {

        var row = 0;
        for(var j=0; j<4;j++)
        {

            var col = 0;
            for (var i = 0; i < 9 && col < bubblesPerRow; ++i, ++col)
            {

                var bubble_type_idx = Random.Range(0, 6);
                if (bubble_type_idx >= 0 && bubble_type_idx < bubbleTypes.Length)
                {
                    var bubble = GenerateBubble(bubble_type_idx);
                    var slot_idx = col + row * bubblesPerRow;

                    slots[slot_idx].bubble = bubble;
                }
            }

            row++;
        }
    }

    private void ArrangeBubblesInSlots()
    {
        for (int row = 0; row < boardHeightInBubbleRows; ++row)
        {
            for (int col = 0; col < bubblesPerRow; ++col)
            {
                var slot_idx = col + row * bubblesPerRow;
                var local = Grid2Local(new Vector2(col, row));

                if (slots[slot_idx].bubble != null)
                {
                    slots[slot_idx].bubble.transform.localPosition = local;
                }
            }
        }
    }

    private Bubble GenerateBubble(int type_idx = -1)
    {
        if (type_idx < 0)
        {
            type_idx = UnityEngine.Random.Range(0, bubbleTypes.Length);
        }

        var bubble = Instantiate(bubblePrefab);
        bubble.type = bubbleTypes[type_idx];
        bubble.GetComponent<SpriteRenderer>().color = bubble.type.color;
        bubble.transform.SetParent(transform);

        return bubble;
    }

    private void Shoot(Vector2 dir, Bubble bubble)
    {
        currentBubble = bubble;
        StartCoroutine(ShootImpl(dir, bubble));
    }

    private Vector3 Hex2Cube(Vector2 hex_coordinate)
    {
        return new Vector3(hex_coordinate.x, -hex_coordinate.x - hex_coordinate.y, hex_coordinate.y);
    }

    private Vector2 Cube2Hex(Vector3 cube_coordinate)
    {
        return new Vector2(cube_coordinate.x, cube_coordinate.z);
    }

    private Vector3 CubeRound(Vector3 cube_coordinate)
    {
        var rx = Mathf.Round(cube_coordinate.x);
        var ry = Mathf.Round(cube_coordinate.y);
        var rz = Mathf.Round(cube_coordinate.z);

        var x_diff = Mathf.Abs(rx - cube_coordinate.x);
        var y_diff = Mathf.Abs(ry - cube_coordinate.y);
        var z_diff = Mathf.Abs(rz - cube_coordinate.z);

        if (x_diff > y_diff && x_diff > z_diff)
        {
            rx = -ry - rz;
        }
        else if (y_diff > z_diff)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }

        return new Vector3(rx, ry, rz);
    }

    private Vector2 HexRound(Vector2 hex_coordinate)
    {
        return Cube2Hex(CubeRound(Hex2Cube(hex_coordinate)));
    }

    private Vector3 Local2Cube(Vector2 local_coordinate)
    {
        local_coordinate.x = local_coordinate.x - bubbleRadius;
        local_coordinate.y = -local_coordinate.y - hexagonSize;

        var q = (local_coordinate.x * Mathf.Sqrt(3) / 3 - local_coordinate.y / 3) / hexagonSize;
        var r = local_coordinate.y * 2 / 3 / hexagonSize;

        return CubeRound(Hex2Cube(new Vector2(q, r)));
    }

    private Vector2 Cube2Grid(Vector3 cube_coordinate)
    {
        return new Vector2(cube_coordinate.x + (cube_coordinate.z - ((int)cube_coordinate.z & 1)) / 2,
            cube_coordinate.z);
    }

    private Vector2 Local2Grid(Vector2 local_coordinate)
    {
        return Cube2Grid(Local2Cube(local_coordinate));
    }

    private Vector2 Grid2Local(Vector2 grid_coordinate)
    {
        var x = hexagonSize * Mathf.Sqrt(3) * (grid_coordinate.x + 0.5 * ((int)grid_coordinate.y & 1));
        var y = hexagonSize * 3 / 2 * grid_coordinate.y;

        return new Vector2((float)x + bubbleRadius, -y - hexagonSize);
    }

    private int Grid2Index(Vector2 grid_coordinate)
    {
        return (int)(grid_coordinate.x + grid_coordinate.y * bubblesPerRow);
    }

    private bool IsGridCoordValid(Vector2 grid_coordinate)
    {
        return grid_coordinate.x >= 0 && grid_coordinate.x < bubblesPerRow
            && grid_coordinate.y >= 0 && grid_coordinate.y < boardHeightInBubbleRows;
    }

    private BubbleCollision BubbleMarching(Vector2 start, Vector2 dir, float step)
    {
        BubbleCollision result = null;
        var current = start;

        if (dir == Vector2.zero)
        {
            return null;
        }

        while (true)
        {
            current += dir * step;

            if (current.magnitude > 1000)
            {
                return new BubbleCollision
                {
                    isSticking = false,
                    grid = Local2Grid(current)
                };
            }

            if (CollisionTest(current, dir, out result))
            {
                if (Local2Grid(start) == Local2Grid(Grid2Local(result.grid)))
                {
                    Debug.Log("!!!");
                }

                return result;
            }
        }
    }

    private bool IsSlotIndexValid(int index)
    {
        return index >= 0 && index < slots.Length;
    }

    private bool CollisionTest(Vector3 local, Vector2 dir, out BubbleCollision collision)
    {
        var grid_coordinate = Local2Grid(local);
        collision = null;

        Vector2[] neighbourGridOffsets = ((int)grid_coordinate.y & 1) == 0 ?
            neighbourGridOffsetsForEvenRow : neighbourGridOffsetsForOddRow;

        var potential_collisions = new List<KeyValuePair<Vector2, float>>();
        var p2 = new List<KeyValuePair<Vector2, float>>();

        foreach (var offset in neighbourGridOffsets)
        {
            var neighbour_grid_coord = grid_coordinate + offset;
            var neighbour_local_coord = Grid2Local(neighbour_grid_coord);
            var distance_between_bubble_and_neighbour = Vector2.Distance(local, neighbour_local_coord);

            if (distance_between_bubble_and_neighbour < bubbleRadius * 2 * collideThreshold)
            {
                potential_collisions.Add(new KeyValuePair<Vector2, float>(neighbour_grid_coord,
                    distance_between_bubble_and_neighbour));
                p2.Add(new KeyValuePair<Vector2, float>(neighbour_local_coord, distance_between_bubble_and_neighbour));
            }
        }

        var prioritized_potential_colliders_grid = potential_collisions.OrderBy(t => t.Value).Select(t => t.Key).ToArray();
        var p22 = p2.OrderBy(t => t.Value).Select(t => t.Key).ToArray();

        foreach (var potential_collider_grid in prioritized_potential_colliders_grid)
        {
            var is_colliding_with_upper_wall = potential_collider_grid.y < 0.0f;
            var is_colliding_with_left_wall = potential_collider_grid.x < 0.0f && dir.x < 0.0f;
            var is_colliding_with_right_wall = potential_collider_grid.x >= bubblesPerRow && dir.x > 0.0f;
            var is_colliding_with_side_walls = is_colliding_with_left_wall || is_colliding_with_right_wall;

            var slot_index = Grid2Index(potential_collider_grid);

            if (is_colliding_with_side_walls)
            {
                // Colliding with a wall...
                collision = new BubbleCollision
                {
                    isSticking = false,
                    grid = grid_coordinate,
                    collidingPointNormal = is_colliding_with_left_wall ? Vector2.right : Vector2.left
                };

                return true;
            }
            else if (is_colliding_with_upper_wall || IsSlotIndexValid(slot_index) && slots[slot_index].bubble != null)
            {
                // Colliding with another bubble...
                collision = new BubbleCollision
                {
                    isSticking = true,
                    grid = grid_coordinate,
                    collidingPointNormal = Vector3.zero
                };

                return true;
            }
        }

        return false;
    }

    public void OnDrawGizmos()
    {
        Gizmos.color = Color.green;

        for (int i = 0; i < bubblesPerRow; ++i)
        {
            for (int j = 0; j < boardHeightInBubbleRows; ++j)
            {
                var local = Grid2Local(new Vector2(i, j));
                Gizmos.DrawWireSphere(local + (Vector2)transform.position, bubbleRadius);
            }
        }

        if (currentBubble != null)
        {
            var grid = Local2Grid(currentBubble.transform.localPosition);
            var normalized_local_for_current_bubble = Grid2Local(grid);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(normalized_local_for_current_bubble + (Vector2)transform.position, bubbleRadius);
        }
    }

    private IEnumerator MoveTo(Transform obj, Vector2 target, float speed)
    {
        while ((Vector2)obj.localPosition != target)
        {
            obj.localPosition = Vector2.MoveTowards(obj.localPosition,
                target, speed * Time.deltaTime);

            yield return null;
        }

        yield break;
    }

    private List<BubbleSlot> CollectChainedBubbleSlots(int starting_col, int starting_row, Bubble.BubbleType chaining_type)
    {
        var chained_bubble_slots = new List<BubbleSlot>();
        FloodFill(starting_col, starting_row,
            slot => slot.bubble != null && slot.bubble.type == chaining_type && !chained_bubble_slots.Contains(slot),
            slot =>
            {
                chained_bubble_slots.Add(slot);
            });

        return chained_bubble_slots;
    }

    private IEnumerable<BubbleSlot> CollectDroppingBubbleSlots(int next_bubble_generation)
    {
        for (int col = 0; col < bubblesPerRow; ++col)
        {
            FloodFill(col, 0,
                slot => slot.bubble != null && slot.generation != next_bubble_generation,
                slot =>
                {
                    slot.generation = next_bubble_generation;
                });
        }

        var dropping_bubble_slots = slots.Where(s => s.bubble != null && s.generation < next_bubble_generation);

        return dropping_bubble_slots;
    }

    private IEnumerator Explode(Bubble bubble)
    {
        Destroy(bubble.gameObject);
        yield break;
    }

    private IEnumerator Drop(Bubble bubble)
    {
        while (bubble.transform.position.y > dropTo)
        {
            bubble.transform.position = Vector2.MoveTowards(bubble.transform.position, 
                new Vector2(bubble.transform.position.x, dropTo), dropSpeed * Time.deltaTime);

            yield return null;
        }

        Destroy(bubble.gameObject);
        yield break;
    }

    private IEnumerator ShootImpl(Vector2 dir, Bubble bubble)
    {
        canShoot = false;

        var waypoints = new List<Vector2>();

        var chained_bubbles = new List<Bubble>();
        var dropping_bubbles = new List<Bubble>();

        BubbleCollision collision = BubbleMarching(bubble.transform.localPosition, dir, bubbleRadius);

        var current_waypoint = (Vector2)bubble.transform.localPosition;
        waypoints.Add(Grid2Local(collision.grid));

        while (!collision.isSticking)
        {
            dir = Vector2.Reflect((waypoints.Last() - current_waypoint).normalized, collision.collidingPointNormal);
            collision = BubbleMarching(waypoints.Last(), dir, bubbleRadius);
            current_waypoint = waypoints.Last();
            waypoints.Add(Grid2Local(collision.grid));
        }

        if (!collision.isSticking)
        {
            var start = bubble.transform.localPosition;

            foreach (var end in waypoints)
            {
                Debug.DrawLine(transform.position + start, transform.position + (Vector3)end, Color.cyan, 5.0f);
                start = end;
            }

            canShoot = true;

            yield break;
        }
        else
        {
            if (collision.grid.y < boardHeightInBubbleRows)
            {
                slots[Grid2Index(collision.grid)].bubble = bubble;
                slots[Grid2Index(collision.grid)].generation = currentBubbleGeneration;

                var chained_bubble_slots = CollectChainedBubbleSlots((int)collision.grid.x, (int)collision.grid.y, bubble.type);

                if (chained_bubble_slots.Count >= bubbleChainThreshold)
                {
                    foreach (var slot in chained_bubble_slots)
                    {
                        chained_bubbles.Add(slot.bubble);
                        slot.bubble = null;
                    }

                    var dropping_bubble_slots = CollectDroppingBubbleSlots(++currentBubbleGeneration);
                    foreach (var slot in dropping_bubble_slots)
                    {
                        dropping_bubbles.Add(slot.bubble);
                        slot.bubble = null;
                    }
                }
            }
            else
            {
                Debug.Log("You lose!");
            }
        }

        foreach (var current_end in waypoints)
        {
            yield return StartCoroutine(MoveTo(bubble.transform, current_end, bubbleShootingSpeed));
        }

        foreach (var cb in chained_bubbles)
        {
            StartCoroutine(Explode(cb));
        }

        foreach (var db in dropping_bubbles)
        {
            StartCoroutine(Drop(db));
        }

        canShoot = true;

        yield break;
    }

    private void ReloadCanon()
    {
        nextBubble = GenerateBubble();
        nextBubble.transform.position = canon.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (canShoot)
            {
                var click_position = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                var canon_position = canon.transform.position;

                var fire_direction = click_position - canon_position;

                if (fire_direction.y > 0.0f)
                {
                    fire_direction = (fire_direction != Vector3.zero) ? fire_direction.normalized : Vector3.up;

                    Shoot(fire_direction, nextBubble);
                    ReloadCanon();
                }
            }
        }
    }
}
