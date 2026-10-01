# Automated Conveyor & Sorting Cell Digital Twin (realvirtual.io MCP)

[![Unity 6](https://img.shields.io/badge/Unity-6000.3.25f1-black?logo=unity)](https://unity.com/)
[![MCP Server](https://img.shields.io/badge/MCP-FastMCP%202.0-blue)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Digital Twin](https://img.shields.io/badge/Industry%204.0-Digital%20Twin-orange)]()

An industrial digital twin project built in **Unity 6** and connected via the **realvirtual Model Context Protocol (MCP)** ecosystem. Enables AI agents (such as Antigravity, Claude, and Cursor) to monitor, inspect, and autonomously control physical factory floor equipment in real time.

---

## 📸 Simulation Showcase

![Cell Overview](docs/images/overview.jpg)
*Figure 1: Full Automated Sorting Cell in nominal production with live SCADA HUD, Pass Accumulation, Diverter Chute, and 3-Tier Andon Light.*

![Machine Vision Gantry](docs/images/vision.jpg)
*Figure 2: Cognex/Keyence-style Machine Vision scanner with projected cyan laser sheet and optical photo-eye sensor.*

---

## 🏗️ Architecture Overview

```
                          +------------------------------+
                          |    AI Agent (MCP Client)     |
                          | (Antigravity / Claude / etc) |
                          +--------------+---------------+
                                         |
                             JSON-RPC (stdio transport)
                                         v
                          +------------------------------+
                          | Python FastMCP Server Bridge |
                          |   (unity_mcp_server.py)      |
                          +--------------+---------------+
                                         |
                             WebSocket (Port 18711 /mcp)
                                         v
             +-------------------------------------------------------+
             |              Unity 6 Editor & Simulation              |
             |  - Infeed Conveyor Line (-5.8m to +5.2m)              |
             |  - Machine Vision Inspection Gantry & Laser Sheet     |
             |  - Optical Photo-Eye Retro-Reflector Sensor          |
             |  - Pneumatic Pusher Sorting Actuator & Guide Fence   |
             |  - 90-Degree Divert Chute & Industrial Scrap Tote    |
             |  - Pass Accumulation Table Buffer                    |
             |  - 3-Tier Industrial Andon Stack Light               |
             |  - Live Real-time SCADA HUD Dashboard                |
             +-------------------------------------------------------+
```

---

## ⚙️ Physical Cell Features

1. **Infeed & Transport Conveyor**:
   - Aluminum extruded framing with adjustable leveling pads.
   - High-friction transport belt with driven end drum rollers.
   - Dual safety-yellow guide rails with sorting cutouts.
   - 3-Phase motor drive representation.

2. **Machine Vision Inspection Gantry (X = -0.6m)**:
   - Rigid overhead gantry bridge.
   - High-speed industrial vision camera with integrated LED illuminator.
   - Dynamic cyan laser scanning sheet plane across the transport path.

3. **Pneumatic Sorting Actuator Station (X = +1.8m)**:
   - Industrial pneumatic cylinder barrel with chrome piston rod.
   - High-visibility polymer pusher head with rubber dampener bumper.
   - Parametric stroke actuation with realistic extension curves and rapid spring-assisted return.

4. **Reject Divert Chute & Scrap Tote (Z = -1.8m)**:
   - 45-degree mechanical diverter guide fence.
   - Low-friction gravity sheet chute feeding into a heavy-duty industrial scrap tote bin.

5. **3-Tier Industrial Andon Stack Light**:
   - 🔴 **Red**: Emergency Stop / System Interlock Fault
   - 🟡 **Amber**: Defect detected / Pneumatic actuator actively sorting
   - 🟢 **Green**: Nominal automated production

6. **Interactive SCADA HUD Dashboard**:
   - Real-time line speed, throughput, pass count, reject count, and defect percentage.
   - Live I/O indicators (Vision Sensor, Pusher Extended/Retracted, Andon State).
   - Clickable workpiece inspector displaying live telemetry.

---

## 🛠️ Realvirtual MCP Tools

The system exposes high-level industrial digital twin endpoints through the realvirtual MCP bridge:

| Tool Name | Parameters | Description |
|---|---|---|
| `cell_get_telemetry` | *none* | Retrieves complete JSON telemetry (speed, parts count, defect rate, sensor signals, actuator stroke). |
| `cell_set_speed` | `speed: float` (0.4 to 4.0 m/s) | Adjusts conveyor line transport speed in real time. |
| `cell_inject_defect` | `defectType: string` | Queues an intentional flaw (`Dimension`, `Surface Flaw`) for sorting validation. |
| `cell_trigger_pusher` | *none* | Manually actuates the pneumatic sorting piston. |
| `cell_toggle_emergency_stop` | `engage: bool` | Halts or releases the emergency stop safety circuit. |
| `cell_reset_stats` | *none* | Resets all throughput, pass, and reject counters. |
| `cell_set_camera` | `preset: string` | Switches camera view (`overview`, `vision`, `pusher`, `chute`). |
| `cell_spawn_workpiece` | `defective: bool`, `defectType: string` | Spawns a custom workpiece with specified quality parameters. |

---

## 🚀 Getting Started

### Prerequisites
- **Unity 6** (Recommended: `6000.0` or higher, e.g. `6000.3.25f1`) with Universal Render Pipeline (URP).
- **Python 3.10+** (if running the external MCP bridge outside Unity).

### 1. Clone the Repository
```bash
git clone https://github.com/<your-username>/DigitalTwinUnity.git
cd DigitalTwinUnity
```

### 2. Open in Unity
1. Launch **Unity Hub**.
2. Click **Add** -> **Add project from disk** and select the cloned folder.
3. Open the project with Unity 6.
4. Open the scene at `Assets/Scenes/ConveyTwin_Main.unity`.
5. Press **Play** ▶️ to run the simulation!

### 3. Connect to MCP Clients (Claude Desktop / Antigravity / Cursor)
Add the server configuration to your `claude_desktop_config.json` or `mcp_config.json`:

```json
{
  "mcpServers": {
    "realvirtual-unity": {
      "command": "python",
      "args": [
        "<PATH_TO_PROJECT>/Assets/StreamingAssets/realvirtual-MCP/unity_mcp_server.py",
        "--mode", "stdio",
        "--port", "18711"
      ]
    }
  }
}
```

---

## 📁 Repository Structure

```
DigitalTwinUnity/
├── Assets/
│   ├── Scenes/
│   │   └── ConveyTwin_Main.unity           # Main configured digital twin scene
│   ├── Scripts/
│   │   ├── AutomatedSortingCellTwin.cs     # Core digital twin simulation logic & MCP tools
│   │   ├── SortingCell.asmdef              # Isolated assembly definition
│   │   └── Editor/
│   │       ├── AutomatedSortingCellEditor.cs # Menu item for one-click setup
│   │       └── SortingCell.Editor.asmdef
│   └── StreamingAssets/
│       └── realvirtual-MCP/                # Python MCP server bridge
├── Packages/
│   ├── io.realvirtual.mcp/                 # Realvirtual MCP Package (MIT)
│   ├── manifest.json
│   └── packages-lock.json
├── ProjectSettings/                        # Unity physics, tag, and project configurations
├── docs/
│   └── images/                             # Showcase screenshots
├── .gitignore                              # Clean Unity gitignore configuration
├── LICENSE                                 # MIT License
└── README.md                               # Project documentation
```

---

## 📄 License
This project is licensed under the [MIT License](LICENSE).
realvirtual MCP components are copyright (c) realvirtual GmbH under MIT License.
