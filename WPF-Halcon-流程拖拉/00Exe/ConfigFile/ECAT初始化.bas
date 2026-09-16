
'*******************************************************ECAT总线初始化
'''''''''

global CONST BUS_TYPE = 0				 '总线类型。用于上位机区分当前模式
global CONST MAX_AXISNUM = 4			 '最大轴数
global CONST Bus_Slot	= 0				'槽位号0
global CONST Bus_AxisStart	 = 0		'总线轴起始轴号

global Bus_InitStatus			'总线初始化完成状态
Bus_InitStatus = -1
global  Bus_TotalAxisnum		'检查扫描的总轴数

delay(8000)				'延时3S等待驱动器上电
'**********************初始化ECAT总线 
Ecat_Init()

end

global sub Ecat_Init()
	for i=0 to MAX_AXISNUM - 1		'初始化还原轴类型					
		AXIS_ENABLE(i) = 0
		atype(i)=0	
	next
		
	Bus_InitStatus = -1
	Bus_TotalAxisnum = 0	
	SLOT_STOP(Bus_Slot)
	delay(200)
	slot_scan(Bus_Slot)											'开始扫描
	if return then 
		
		?"总线扫描成功","连接设备数："NODE_COUNT(Bus_Slot)
		?
		?"开始映射轴号"
		for i=0 to NODE_COUNT(Bus_Slot)-1						'遍历总线下所有从站节点

			if NODE_AXIS_COUNT(Bus_Slot,i) <>0 then			'判断当前节点是否有电机
				for j=0 to NODE_AXIS_COUNT(Bus_Slot,i)-1
					AXIS_ADDRESS(Bus_AxisStart+i)=Bus_TotalAxisnum+1			'映射轴号
					ATYPE(Bus_AxisStart+i)=65									'设置控制模式 65-位置 66-速度 67-转矩 
					DRIVE_PROFILE(Bus_AxisStart+i)= 4							'设置PROFILE功能
					disable_group(Bus_AxisStart+i)								'每轴单独分组

					DRIVE_IO(Bus_AxisStart+i) = 128 + (Bus_AxisStart+i)*16		'映射驱动器上的IO状态
					REV_IN(Bus_AxisStart+i) = 128 + (Bus_AxisStart+i)*16 
					FWD_IN(Bus_AxisStart+i) = 129 + (Bus_AxisStart+i)*16
					DATUM_IN(Bus_AxisStart+i) = 130 + (Bus_AxisStart+i)*16
					INVERT_IN(128 + (Bus_AxisStart+i)*16,ON)
					INVERT_IN(129 + (Bus_AxisStart+i)*16,ON)
					INVERT_IN(130 + (Bus_AxisStart+i)*16,ON)
					
					Bus_TotalAxisnum=Bus_TotalAxisnum+1							'总轴数+1
				next
			endif
			
		next
		
		?"轴号映射完成","连接总轴数："Bus_TotalAxisnum
		wa 2000
		
		SLOT_START(Bus_Slot)						'启动总线
		if return then 
			?"总线开启成功"
			
			?"开始清除驱动器错误(根据驱动器数据字典设置)"
			for i= Bus_AxisStart to Bus_AxisStart + Bus_TotalAxisnum - 1 
				
				DRIVE_CONTROLWORD(i)=128					'根据驱动器数据字典
				wa 100
				DRIVE_CONTROLWORD(i)=6
				wa 100
				DRIVE_CONTROLWORD(i)=15
				wa 100
			next
			
			?"驱动器错误清除完成"
			wa 100

			?"清除控制器错误"
			datum(0)
			DRIVE_CLEAR(0)
			?"控制器错误清除完成"
			wa 100
						
			?"轴使能准备"
			for i= Bus_AxisStart to Bus_AxisStart + Bus_TotalAxisnum - 1
				base(i)
				AXIS_ENABLE=1
			next
			wdog=1											'使能总开关
			Bus_InitStatus  = 1
			?"轴使能完成"
		else
			?"总线开启失败"
			Bus_InitStatus = 0
		endif
		
	else
		?"总线扫描失败"
		Bus_InitStatus = 0
	endif
end sub