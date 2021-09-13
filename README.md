# Communicative Learning with Natural Gestures for Embodied Navigation Agents with Human-in-the-Scene
By Qi Wu, Cheng-Ju (Jimmy) Wu, Yixin Zhu, and Jungseock Joo (To appear in IROS 2021)

[IROS 2021 Paper](https://arxiv.org/pdf/2108.02846.pdf) | [Website](https://sites.google.com/view/iros2021-gesthor/home) | [Video](https://youtu.be/UAqGMQchEEg) | [BibTex](#citing)

This is the source code repository for Ges-THOR framework from this IROS paper. The experiments are tested on Ubuntu 18.04 and 20.04.

## Citing

If you find this project useful in your research, please consider citing:

```
@inproceedings{wu2021communicative,
      title={Communicative Learning with Natural Gestures for Embodied Navigation Agents with Human-in-the-Scene}, 
      author={Qi Wu and Cheng-Ju Wu and Yixin Zhu and Jungseock Joo},
      booktitle={International Conference on Intelligent Robotics and Systems (IROS)}
      year={2021},
}
```

## Setup
First, clone the repository with `git clone https://github.com/qiwu57kevin/iros2021-gesthor.git && cd iros2021-gesthor`.

You will find two directories:
- `unity`: this directory contains the Unity local build where the user can interact with the simulation environment.
- `allenact`: this directory contains training scripts based on [AllenAct](https://github.com/allenai/allenact/). You could start training and evaluation without opening the Unity Editor.

If you want to try the interative feature of our simulation framework, check [this](GESTHOR-UNITY.md) document; if you only want to train the navigation agent with gestures, please `cd allenact` and follow steps below.

You can download our dataset from [here](https://drive.google.com/file/d/1ccUac_mGPUyYbIuYDHfrkLYcCEaJV7Up/view?usp=sharing). Unpack this package under `datasets` folder. The `ithor-objectnav-gesture` folder after unpacking contains:
- `train`, `val`, `test` folder for 3 different stages
- In each of the above folder, you will see 3 subfolders:
    - `episodes`: it contains episodic information for all scenes
    - `motions`: it contains motion files for referencing gestures
    - `intervention_gestures`: it contains motion files for intervention gestures

After downloading the datasets, you should install all necessary python packages for training. We recommend you to install a conda environment and follow the instructions [here](https://allenact.org/installation/installation-allenact/#installing-a-conda-environment) provided by AllenAct.

## Training

Here we only provide running scripts for training. If you want to know more details about our database, please refer to [AllenAct website](https://allenact.org/).

The experiment config file is `projects/ithor_gesture/objectnav_ithor_rgbd_resnetgru_ddppo_gesture.py`.

To start a training, say you want to run an agent with referencing gestures in the kichen, try running the following scripts:

```
python3 main.py \
projects/ithor_gesture/objectnav_ithor_rgbd_resnetgru_ddppo_gesture.py \
-o storage/example_experiment \
-s 12345 \ 
--config_kwargs "{'recording_percentage':1.0, \
                  'use_gesture':True \
		  'add_intervention':False, \
		  'room_type':'kitchen'}"
```

A few notes on the scripts:
- With `-o storage/example_experiment` we set the output folder into which results and logs will be saved.
- With `-s 12345` we set the random seed.
- In `config_kwargs`:
    - `recording_percentage` refers to how many samples we need from the dataset. If you only want half of it, just write 0.5. (default `1.0`)
    - `use_gesture`: whether to use referencing gestures. (default `false`)
    - `add_intervention`: whether to use intervention gestures. This could be used with referencing gestures in the same episode. (default `false`)
    - `room_types`: we have 5 selections: kitchen, livingroom, bedroom, bathroom, and all, which represents all scenes. (default `"all"`)

## Evaluation

To evaluate your trained model, first look for your checkpoint. For example, your checkpoint is `storage/my_checkpoint.pt` based on your experiment above in [training](#training), you could run the following script:

```
python3 main.py \
projects/ithor_gesture/objectnav_ithor_rgbd_resnetgru_ddppo_gesture.py \
-o storage/example_experiment \
-s 12345 \ 
--config_kwargs "{'recording_percentage':1.0, \
                  'use_gesture':True \
		  'add_intervention':False, \
		  'room_type':'kitchen'}" \
-c storage/my_checkpoint.pt \
--eval
```

Here, `-c` is to specify which checkpoint model to use, and `--eval` is to mark the process as evaluation--or lese, the training will resume from the checkpoint model and continue.
