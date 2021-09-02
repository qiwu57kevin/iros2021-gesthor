import os
import gym
import glob
import numpy as np
from math import ceil
from abc import ABC
from typing import Sequence, Union, Optional, Dict, Any, List

import torch
import torch.optim as optim
import torch.nn as nn
from torchvision import models
from torch.optim.lr_scheduler import LambdaLR

from allenact.base_abstractions.experiment_config import ExperimentConfig, MachineParams
from allenact.base_abstractions.preprocessor import Preprocessor, SensorPreprocessorGraph
from allenact.base_abstractions.sensor import RGBSensor, DepthSensor, SensorSuite, ExpertActionSensor
from allenact.base_abstractions.task import TaskSampler

from allenact_plugins.robothor_plugin.robothor_sensors import DepthSensorThor
from allenact_plugins.robothor_plugin.robothor_tasks import ObjectGestureNavTask
from allenact_plugins.robothor_plugin.robothor_task_samplers import ObjectGestureNavDatasetTaskSampler
from allenact_plugins.ithor_plugin.ithor_sensors import (
    GestureDatasetSensor,
    HumanPoseSensor,
    RGBSensorThor,
    GoalObjectTypeThorGestureSensor,
    RelativePositionTHORSensor,
)
from allenact_plugins.ithor_plugin.ithor_util import horizontal_to_vertical_fov

from projects.objectnav_baselines.experiments.ithor.objectnav_ithor_base import ObjectNaviThorBaseConfig
from projects.objectnav_baselines.experiments.robothor.objectnav_robothor_base import ObjectNavRoboThorBaseConfig
from projects.objectnav_baselines.models.object_nav_models import (
    ResnetTensorObjectGestureNavActorCritic,
    ResnetTensorObjectNavActorCritic,
)

from allenact.algorithms.onpolicy_sync.losses import PPO
from allenact.algorithms.onpolicy_sync.losses.ppo import PPOConfig
from allenact.utils.experiment_utils import (
    Builder,
    PipelineStage,
    TrainingPipeline,
    LinearDecay,
    evenly_distribute_count_into_bins
)
from allenact.utils.system import get_logger

from allenact.embodiedai.preprocessors.resnet import ResNetPreprocessor


class ObjectNavRoboThorRGBPPOGestureExperimentConfig(ExperimentConfig, ABC):
    """An Object Navigation experiment configuration in iThor with RGBD and gesture input."""

    TARGET_TYPES = tuple(
        sorted(
            [
                'AlarmClock',
                'Apple',
                'ArmChair',
                'Bed',
                'Box',
                'Bread',
                'ButterKnife',
                'Chair',
                'CoffeeTable',
                'Cup',
                'DeskLamp',
                'DiningTable',
                'FloorLamp',
                'Fork',
                'HandTowel',
                'HandTowelHolder',
                'Knife',
                'Laptop',
                'Newspaper',
                'PaperTowelRoll',
                'Plate',
                'Plunger',
                'Poster',
                'Potato',
                'RemoteControl',
                'ShowerCurtain',
                'ShowerDoor',
                'SideTable',
                'Sofa',
                'Spoon',
                'Television',
                'TissueBox',
                'ToiletPaper',
                'ToiletPaperHanger',
                'Tomato',
                'Towel',
                'TowelHolder',
                'Window'
            ]
        )
    )
    
    ROOM_TYPES = tuple(
        sorted(
            [
                "Kitchen",
                "LivingRoom",
                "Bathroom",
                "Bedroom",
            ]
        )
    )
    
    STEP_SIZE = 0.25
    ROTATION_DEGREES = 15.0
    VISIBILITY_DISTANCE = 1.5
    STOCHASTIC = False
    HORIZONTAL_FIELD_OF_VIEW = 90

    CAMERA_WIDTH = 224
    CAMERA_HEIGHT = 224
    SCREEN_SIZE = 224
    MAX_STEPS = 100
    
    NUM_PROCESSES = 10
    TRAIN_GPU_IDS = list(range(torch.cuda.device_count()))
    SAMPLER_GPU_IDS = TRAIN_GPU_IDS
    VALID_GPU_IDS = [torch.cuda.device_count() - 1]
    TEST_GPU_IDS = [torch.cuda.device_count() - 1]
    
    TRAIN_DATASET_DIR = os.path.join(os.getcwd(), "datasets/ithor-objectnav-gesture/train")
    VAL_DATASET_DIR = os.path.join(os.getcwd(), "datasets/ithor-objectnav-gesture/val")
    TEST_DATASET_DIR = os.path.join(os.getcwd(), "datasets/ithor-objectnav-gesture/test")
    
    ADVANCE_SCENE_ROLLOUT_PERIOD = None
    
    THOR_COMMIT_ID = "bad5bc2b250615cb766ffb45d455c211329af17e"
    
    # try:
    #     with open("instruction_tokens.txt", "r") as f:
    #         INSTRUCTION_TOKENS = tuple(sorted(map(lambda a:a.strip('\n'), f.readlines())))
    # except:
    #     raise Exception("Cannot read instruction tokens from the loaded text file.")

    SENSORS = [
        RGBSensorThor(
            height=ObjectNaviThorBaseConfig.SCREEN_SIZE,
            width=ObjectNaviThorBaseConfig.SCREEN_SIZE,
            use_resnet_normalization=True,
            uuid="rgb_lowres",
        ),
        DepthSensorThor(
            height=ObjectNaviThorBaseConfig.SCREEN_SIZE,
            width=ObjectNaviThorBaseConfig.SCREEN_SIZE,
            use_normalization=True,
            uuid="depth_lowres",
        ),
        GoalObjectTypeThorGestureSensor(object_types=TARGET_TYPES,),
        # RelativePositionTHORSensor(uuid="rel_position"),
        GestureDatasetSensor(uuid="gestures"),
        HumanPoseSensor(uuid="human_poses"),
    ]
    
    def __init__(self, **kwargs):
        self.REWARD_CONFIG = {
            "step_penalty": -0.001,
            "goal_success_reward": 1.0,
            "failed_stop_reward": -0.01,
            "collision_reward": -0.005,
            "shaping_weight": 0.0,
        } # TODO Gesture add collision penalty   
        self.recording_percentage=float(kwargs["recording_percentage"])
        self.prediction_percentage=float(kwargs["prediction_percentage"])
        self.add_intervention=bool(kwargs["add_intervention"])
        self.smoothed=bool(kwargs["smoothed"])
        self.room_type=str(kwargs["room_type"])
        self.use_gesture=bool(kwargs["use_gesture"])
        self.random=bool(kwargs["random"]) if "random" in kwargs else False

        # if not self.use_gesture:
        #     self.SENSORS =  self.SENSORS[:4]


    @classmethod
    def tag(cls):
        return "Objectnav-iTHOR-RGBD-Gesture-ResNetGRU-DDPPO"
    
    @classmethod
    def preprocessors(cls) -> Sequence[Union[Preprocessor, Builder[Preprocessor]]]:
        preprocessors = []

        rgb_sensor = next((s for s in cls.SENSORS if isinstance(s, RGBSensor)), None)
        if rgb_sensor is not None:
            preprocessors.append(
                ResNetPreprocessor(
                    input_height=cls.SCREEN_SIZE,
                    input_width=cls.SCREEN_SIZE,
                    output_width=7,
                    output_height=7,
                    output_dims=512,
                    pool=False,
                    torchvision_resnet_model=models.resnet18,
                    input_uuids=[rgb_sensor.uuid],
                    output_uuid="rgb_resnet",
                )
            )

        depth_sensor = next(
            (s for s in cls.SENSORS if isinstance(s, DepthSensor)), None
        )
        if depth_sensor is not None:
            preprocessors.append(
                ResNetPreprocessor(
                    input_height=ObjectNavRoboThorBaseConfig.SCREEN_SIZE,
                    input_width=ObjectNavRoboThorBaseConfig.SCREEN_SIZE,
                    output_width=7,
                    output_height=7,
                    output_dims=512,
                    pool=False,
                    torchvision_resnet_model=models.resnet18,
                    input_uuids=[depth_sensor.uuid],
                    output_uuid="depth_resnet",
                )
            )

        return preprocessors

    # @classmethod
    def create_model(self, **kwargs) -> nn.Module:
        has_rgb = any(isinstance(s, RGBSensor) for s in self.SENSORS)
        has_depth = any(isinstance(s, DepthSensor) for s in self.SENSORS)
        goal_sensor_uuid = next(
            (s.uuid for s in self.SENSORS if isinstance(s, GoalObjectTypeThorGestureSensor)),
            None,
        )
        # rel_position_uuid = next(
        #     (s.uuid for s in self.SENSORS if isinstance(s, RelativePositionTHORSensor)),
        #     None,
        # )
        gesture_sensor_uuid = next(
            (s.uuid for s in self.SENSORS if isinstance(s, GestureDatasetSensor)),
            None,
        )
        human_pose_uuid = next(
            (s.uuid for s in self.SENSORS if isinstance(s, HumanPoseSensor)),
            None,
        )
        
        for s in self.SENSORS:
            if isinstance(s, GestureDatasetSensor):
                s.add_intervention = self.add_intervention
                s.use_gesture = self.use_gesture

        for s in self.SENSORS:
            if isinstance(s, HumanPoseSensor):
                s.use_gesture = self.use_gesture
        
        # if self.use_gesture:
        return ResnetTensorObjectGestureNavActorCritic(
            action_space=gym.spaces.Discrete(len(ObjectGestureNavTask.class_action_names())),
            observation_space=kwargs["sensor_preprocessor_graph"].observation_spaces,
            goal_sensor_uuid=goal_sensor_uuid,
            # rel_position_uuid=rel_position_uuid,
            gesture_sensor_uuid=gesture_sensor_uuid,
            human_pose_uuid=human_pose_uuid,
            rgb_resnet_preprocessor_uuid="rgb_resnet" if has_rgb else None,
            depth_resnet_preprocessor_uuid="depth_resnet" if has_depth else None,
            hidden_size=512,
            goal_dims=512,
            gesture_compressor_hidden_out_dim=512,
            human_pose_hidden_out_dim=512,
            )
        # else:
        #     return ResnetTensorObjectNavActorCritic(
        #         action_space=gym.spaces.Discrete(len(ObjectGestureNavTask.class_action_names())),
        #         observation_space=kwargs["sensor_preprocessor_graph"].observation_spaces,
        #         goal_sensor_uuid=goal_sensor_uuid,
        #         rgb_resnet_preprocessor_uuid="rgb_resnet" if has_rgb else None,
        #         depth_resnet_preprocessor_uuid="depth_resnet" if has_depth else None,
        #         hidden_size=512,
        #         goal_dims=32,
        #     )
        
    @classmethod
    def env_args(cls):
        assert cls.THOR_COMMIT_ID is not None

        return dict(
            width=cls.CAMERA_WIDTH,
            height=cls.CAMERA_HEIGHT,
            quality="Very Low",
            commit_id=cls.THOR_COMMIT_ID,
            continuousMode=True,
            applyActionNoise=cls.STOCHASTIC,
            agentType="stochastic",
            rotateStepDegrees=cls.ROTATION_DEGREES,
            visibilityDistance=cls.VISIBILITY_DISTANCE,
            gridSize=cls.STEP_SIZE,
            snapToGrid=False,
            agentMode="default",
            fieldOfView=horizontal_to_vertical_fov(
                horizontal_fov_in_degrees=cls.HORIZONTAL_FIELD_OF_VIEW,
                width=cls.CAMERA_WIDTH,
                height=cls.CAMERA_HEIGHT,
            ),
            include_private_scenes=False,
            renderDepthImage=any(isinstance(s, DepthSensorThor) for s in cls.SENSORS),
            # local_executable_path = "/home/kevin57/Unity\ Projects/ithor_env/ithor_env.x86_64",
            # local_executable_path = "/home/qi/ithor_env/ithor_env.x86_64",
        )

    def machine_params(self, mode="train", **kwargs):
        sampler_devices: Sequence[int] = []
        if mode == "train":
            workers_per_device = 1
            gpu_ids = (
                []
                if not torch.cuda.is_available()
                else self.TRAIN_GPU_IDS * workers_per_device
            )
            nprocesses = (
                1
                if not torch.cuda.is_available()
                else evenly_distribute_count_into_bins(self.NUM_PROCESSES, len(gpu_ids))
            )
            sampler_devices = self.SAMPLER_GPU_IDS
        elif mode == "valid":
            nprocesses = 1
            gpu_ids = [] if not torch.cuda.is_available() else self.VALID_GPU_IDS
        elif mode == "test":
            nprocesses = 5 if torch.cuda.is_available() else 1
            gpu_ids = [] if not torch.cuda.is_available() else self.TEST_GPU_IDS
        else:
            raise NotImplementedError("mode must be 'train', 'valid', or 'test'.")

        sensors = [*self.SENSORS]
        if mode != "train":
            sensors = [s for s in sensors if not isinstance(s, ExpertActionSensor)]

        sensor_preprocessor_graph = (
            SensorPreprocessorGraph(
                source_observation_spaces=SensorSuite(sensors).observation_spaces,
                preprocessors=self.preprocessors(),
            )
            if mode == "train"
            or (
                (isinstance(nprocesses, int) and nprocesses > 0)
                or (isinstance(nprocesses, Sequence) and sum(nprocesses) > 0)
            )
            else None
        )

        return MachineParams(
            nprocesses=nprocesses,
            devices=gpu_ids,
            sampler_devices=sampler_devices
            if mode == "train"
            else gpu_ids,  # ignored with > 1 gpu_ids
            sensor_preprocessor_graph=sensor_preprocessor_graph,
        )

    # @classmethod
    def make_sampler_fn(self, **kwargs) -> TaskSampler:
        return ObjectGestureNavDatasetTaskSampler(self.recording_percentage, self.prediction_percentage, self.smoothed, **kwargs)

    @staticmethod
    def _partition_inds(n: int, num_parts: int):
        return np.round(np.linspace(0, n, num_parts + 1, endpoint=True)).astype(
            np.int32
        )

    def _get_sampler_args_for_scene_split(
        self,
        scenes_dir: str,
        process_ind: int,
        total_processes: int,
        devices: Optional[List[int]],
        seeds: Optional[List[int]],
        deterministic_cudnn: bool,
        include_expert_sensor: bool = True,
        allow_oversample: bool = False,
    ) -> Dict[str, Any]:
        path = os.path.join(scenes_dir, "*.json.gz")
        
        scenes = [scene.split("/")[-1].split(".")[0] for scene in glob.glob(path)]
        scenes = list(sorted(scenes, key=lambda x: int(x.strip("FloorPlan"))))
        scene_len = len(scenes)
        # Get scenes according to room type
        if self.room_type == "all":
            scenes = scenes[:]
        elif self.room_type == "kitchen":
            scenes = scenes[:scene_len//4]
        elif self.room_type == "livingroom":
            scenes = scenes[scene_len//4:scene_len//2]
        elif self.room_type == "bedroom":
            scenes = scenes[scene_len//2:scene_len*3//4]
        elif self.room_type == "bathroom":
            scenes = scenes[scene_len*3//4:]
        else:
            raise ValueError("Please give a valid room type")

        if len(scenes) == 0:
            raise RuntimeError(
                (
                    "Could find no scene dataset information in directory {}."
                    " Are you sure you've downloaded them? "
                    " If not, see https://allenact.org/installation/download-datasets/ information"
                    " on how this can be done."
                ).format(scenes_dir)
            )

        oversample_warning = (
            f"Warning: oversampling some of the scenes ({scenes}) to feed all processes ({total_processes})."
            " You can avoid this by setting a number of workers divisible by the number of scenes"
        )
        if total_processes > len(scenes):  # oversample some scenes -> bias
            if not allow_oversample:
                raise RuntimeError(
                    f"Cannot have `total_processes > len(scenes)`"
                    f" ({total_processes} > {len(scenes)}) when `allow_oversample` is `False`."
                )

            if total_processes % len(scenes) != 0:
                get_logger().warning(oversample_warning)
            scenes = scenes * int(ceil(total_processes / len(scenes)))
            scenes = scenes[: total_processes * (len(scenes) // total_processes)]
        elif len(scenes) % total_processes != 0:
            get_logger().warning(oversample_warning)

        inds = self._partition_inds(len(scenes), total_processes)

        return {
            "scenes": scenes[inds[process_ind] : inds[process_ind + 1]],
            "object_types": self.TARGET_TYPES,
            "max_steps": self.MAX_STEPS,
            "sensors": [
                s
                for s in self.SENSORS
                if (include_expert_sensor or not isinstance(s, ExpertActionSensor))
            ],
            "action_space": gym.spaces.Discrete(
                len(ObjectGestureNavTask.class_action_names())
            ),
            "seed": seeds[process_ind] if seeds is not None else None,
            "deterministic_cudnn": deterministic_cudnn,
            "rewards_config": self.REWARD_CONFIG,
            "env_args": {
                **self.env_args(),
                # """
                "x_display": (
                     f"0.{devices[process_ind % len(devices)]}"
                     if devices is not None
                     and len(devices) > 0
                     and devices[process_ind % len(devices)] >= 0
                     else None
                 ),
                # """
                # "x_display": "1",
            },
        }

    def train_task_sampler_args(
        self,
        process_ind: int,
        total_processes: int,
        devices: Optional[List[int]] = None,
        seeds: Optional[List[int]] = None,
        deterministic_cudnn: bool = False,
    ) -> Dict[str, Any]:
        res = self._get_sampler_args_for_scene_split(
            scenes_dir=os.path.join(self.TRAIN_DATASET_DIR, "episodes"),
            process_ind=process_ind,
            total_processes=total_processes,
            devices=devices,
            seeds=seeds,
            deterministic_cudnn=deterministic_cudnn,
            allow_oversample=True,
        )
        res["scene_directory"] = self.TRAIN_DATASET_DIR
        res["loop_dataset"] = True
        res["allow_flipping"] = True
        return res

    def valid_task_sampler_args(
        self,
        process_ind: int,
        total_processes: int,
        devices: Optional[List[int]] = None,
        seeds: Optional[List[int]] = None,
        deterministic_cudnn: bool = False,
    ) -> Dict[str, Any]:
        res = self._get_sampler_args_for_scene_split(
            scenes_dir=os.path.join(self.VAL_DATASET_DIR, "episodes"),
            process_ind=process_ind,
            total_processes=total_processes,
            devices=devices,
            seeds=seeds,
            deterministic_cudnn=deterministic_cudnn,
            include_expert_sensor=False,
            allow_oversample=False,
        )
        res["scene_directory"] = self.VAL_DATASET_DIR
        res["loop_dataset"] = False
        return res

    def test_task_sampler_args(
        self,
        process_ind: int,
        total_processes: int,
        devices: Optional[List[int]] = None,
        seeds: Optional[List[int]] = None,
        deterministic_cudnn: bool = False,
    ) -> Dict[str, Any]:
        if self.TEST_DATASET_DIR is None:
            get_logger().warning(
                "No test dataset dir detected, running test on validation set instead."
            )
            return self.valid_task_sampler_args(
                process_ind=process_ind,
                total_processes=total_processes,
                devices=devices,
                seeds=seeds,
                deterministic_cudnn=deterministic_cudnn,
            )

        else:
            res = self._get_sampler_args_for_scene_split(
                scenes_dir=os.path.join(self.TEST_DATASET_DIR, "episodes"),
                process_ind=process_ind,
                total_processes=total_processes,
                devices=devices,
                seeds=seeds,
                deterministic_cudnn=deterministic_cudnn,
                include_expert_sensor=False,
                allow_oversample=False,
            )
            res["env_args"]["all_metadata_available"] = True # TODO Gesture we can log all metrics (sr and spl) by setting this to true WHY?
            res["rewards_config"] = {**res["rewards_config"], "shaping_weight": 0}
            res["scene_directory"] = self.TEST_DATASET_DIR
            res["loop_dataset"] = False
            return res
        
    def training_pipeline(self, **kwargs):
        ppo_steps = int(10000000)
        lr = 3e-4
        num_mini_batch = 1
        update_repeats = 4
        num_steps = 128
        save_interval = 1000000
        log_interval = self.MAX_STEPS*10
        gamma = 0.99
        use_gae = True
        gae_lambda = 0.95
        max_grad_norm = 0.5
        return TrainingPipeline(
            save_interval=save_interval,
            metric_accumulate_interval=log_interval,
            optimizer_builder=Builder(optim.Adam, dict(lr=lr)),
            num_mini_batch=num_mini_batch,
            update_repeats=update_repeats,
            max_grad_norm=max_grad_norm,
            num_steps=num_steps,
            named_losses={"ppo_loss": PPO(**PPOConfig)},
            gamma=gamma,
            use_gae=use_gae,
            gae_lambda=gae_lambda,
            advance_scene_rollout_period=self.ADVANCE_SCENE_ROLLOUT_PERIOD,
            pipeline_stages=[
                PipelineStage(loss_names=["ppo_loss"], max_stage_steps=ppo_steps)
            ],
            lr_scheduler_builder=Builder(
                LambdaLR, {"lr_lambda": LinearDecay(steps=ppo_steps)}
            ),
        )
