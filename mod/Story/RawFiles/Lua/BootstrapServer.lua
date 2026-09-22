--- EoC Trainer - server side bootstrap.
--- This file is loaded by the Divinity Script Extender (Osiris Extender) once the
--- module is starting up. It only wires the trainer modules together; the real
--- work happens in EocTrainer/*.lua, which is polled from the "Tick" event.

Ext.Require("EocTrainer/Enums.lua")
Ext.Require("EocTrainer/Bridge.lua")
Ext.Require("EocTrainer/State.lua")
Ext.Require("EocTrainer/Ops.lua")
Ext.Require("EocTrainer/Main.lua")
