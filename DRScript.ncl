undef("TotalSIFCalc")
function TotalSIFCalc()
local f,sifn,sifs,tsifn,tisfs,alist
begin
    f = addfile("","r")
    f@_FillValue = f@missing_value
    sifn -> sea_ice_fraction[/0,{0:90},{-180:180}/]
    sifs -> sea_ice_fraction[/0,{-90:0},{-180:180}/]
    tsifn = dim_sum_n(sifn, 0) 
    tsifs = dim_sum_n(sifs, 0) 
    alist = [/tsifn, tsifs/]
    write_table("", "w", alist, "%b%b")
end