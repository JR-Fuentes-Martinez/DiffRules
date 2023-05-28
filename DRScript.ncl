begin
    f = addfile("","r")
    sifn=f->sea_ice_fraction(0,{0:90},:)
    sifs=f->sea_ice_fraction(0,{-90:0},:)
    tsifn = dim_sum_n(sifn, 0) 
    tsifs = dim_sum_n(sifs, 0) 
    alist = [/tsifn, tsifs/]
    write_table("", "w", alist, "%d%d")
end
