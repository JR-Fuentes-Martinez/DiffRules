begin
    f = addfile("","w")
    f@_Fillvalue = 0B
    print(f@_Fillvalue)
    sifn=f->sea_ice_fraction(0,{0:90},:)
    sifs=f->sea_ice_fraction(0,{-90:0},:)
    tsifn = sum( sifn(lat|:,lon|:) )
    tsifs = sum( sifs(lat|:,lon|:) )
    alist = [/tsifn, tsifs/]
    write_table("", "w", alist, "%d%d")
end
