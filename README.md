# TerraRover-Gen: Autonomous Rover Navigation via Reinforcement Learning 🤖

<img width="940" height="302" alt="BumpyGround" src="https://github.com/user-attachments/assets/83a97c16-7b8a-478b-9247-bd1cc32ded7f" />

## 📌 Overview
TerraRover-Gen is a 3D simulation and reinforcement learning environment developed in **Unity 6**. It features a differential ROVER (Husky) trained to autonomously navigate procedurally generated, uneven terrains using a Proximal Policy Optimization (PPO) agent. 

This project bridges the gap between software engineering, 3D physics simulation, and artificial intelligence, showcasing how simulated environments can be used to model and train robotic behaviors safely before real-world deployment.

## 🛠️ Tech Stack
- **Engine & Logic:** Unity 6000.3.6f1, C#
- **Machine Learning:** Unity ML-Agents (4.0.1), Python, PPO Algorithm
- **Robotics Integration:** Unity Robotics URDF Importer (v0.5.2)
- **Data Analysis:** Python (Pandas, Matplotlib) for evaluation pipelines

## ✨ Key Features & Engineering
- **Procedural Terrain Generation:** Programmed a dynamic system to generate parameterized terrains, allowing fine-tuning of roughness, slopes, obstacles, and pits.
- **Physics-Based Simulation:** Implemented realistic rover physics using URDF and ArticulationBody components.
- **Zero-Shot Generalization Training:** Trained a frozen PPO policy capable of adapting to unseen terrain families.
- **Automated Evaluation Pipeline:** Developed Python scripts to verify pairing invariants, compute results, and generate quantitative data against a baseline heuristic controller.

## 🎓 Academic Context & Provenance
This project was originally developed as my **BSc Computer Engineering Final Degree Project** at UCAM. 

It also serves as the reproducible evaluation package for the academic manuscript *"TerraRover-Gen: A Controlled Study of Zero-Shot Terrain-Family Generalization for Rover Navigation"*, co-authored with my project director, D. Antonio Serrano Fernández.

## 🚀 Reproducing the Evaluation Data
To run the statistical analysis and regenerate the data tables/figures from the paper:

```bash
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r Analysis/requirements.txt
python Analysis/paper_analysis.py
python Analysis/paper_figures.py
